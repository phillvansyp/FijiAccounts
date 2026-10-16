using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record XeroImportPreparationResult(
    int DatasetCount,
    int SourceRecordCount,
    int ReadyCount,
    int NeedsMappingCount,
    int WarningCount,
    int BlockingCount);

public sealed record XeroImportWorkspace(
    int DatasetCount,
    int SourceRecordCount,
    int ReadyCount,
    int NeedsMappingCount,
    int ImportedCount,
    int WarningCount,
    int BlockingCount,
    IReadOnlyList<XeroMigrationMapping> Mappings,
    IReadOnlyList<XeroMigrationIssue> Issues,
    XeroMigrationImportRun? LastRun);

public sealed record XeroMappingTarget(Guid? Id, string Value, string Label, string EntityType);

/// <summary>
/// Converts immutable Xero snapshots into a normalised, idempotent staging index.
/// The index is deliberately separate from live accounting records: mappings and
/// exceptions must be reviewable before any posting operation is enabled.
/// </summary>
public sealed class XeroImportPreparationService(
    ApplicationDbContext db,
    TenantAccessService access,
    IImmutableDocumentStore storage)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyDictionary<string, string> RootProperties =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["organisation"] = "Organisations", ["accounts"] = "Accounts",
            ["tax-rates"] = "TaxRates", ["currencies"] = "Currencies",
            ["tracking"] = "TrackingCategories", ["contacts"] = "Contacts",
            ["items"] = "Items", ["sales-invoices"] = "Invoices", ["bills"] = "Invoices",
            ["sales-credits"] = "CreditNotes", ["supplier-credits"] = "CreditNotes",
            ["payments"] = "Payments", ["batch-payments"] = "BatchPayments",
            ["prepayments"] = "Prepayments", ["overpayments"] = "Overpayments",
            ["bank-transactions"] = "BankTransactions", ["bank-transfers"] = "BankTransfers",
            ["manual-journals"] = "ManualJournals", ["purchase-orders"] = "PurchaseOrders",
            ["quotes"] = "Quotes", ["repeating-invoices"] = "RepeatingInvoices",
            ["budgets"] = "Budgets", ["assets"] = "Assets", ["asset-types"] = "AssetTypes",
            ["asset-settings"] = "Settings", ["trial-balance"] = "Reports",
            ["balance-sheet"] = "Reports", ["profit-loss"] = "Reports",
            ["aged-receivables"] = "Reports", ["aged-payables"] = "Reports"
        };

    private static readonly HashSet<string> ControlDatasets = new(StringComparer.OrdinalIgnoreCase)
    {
        "organisation", "asset-settings", "trial-balance", "balance-sheet", "profit-loss",
        "aged-receivables", "aged-payables"
    };

    private static readonly IReadOnlyDictionary<string, string> LiveImportDatasets =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sales-invoices"] = "sales invoices", ["bills"] = "supplier bills",
            ["sales-credits"] = "sales credit notes", ["supplier-credits"] = "supplier credit notes",
            ["payments"] = "payments", ["batch-payments"] = "batch payments",
            ["prepayments"] = "prepayments", ["overpayments"] = "overpayments",
            ["bank-transactions"] = "bank transactions", ["bank-transfers"] = "bank transfers",
            ["manual-journals"] = "manual journals", ["purchase-orders"] = "purchase orders",
            ["quotes"] = "quotes", ["repeating-invoices"] = "repeating invoices",
            ["budgets"] = "budgets", ["assets"] = "fixed assets"
        };

    public async Task<XeroImportWorkspace> GetWorkspaceAsync(
        string userId, Guid organisationId, CancellationToken cancellationToken = default)
    {
        await DemandAccessAsync(userId, organisationId);
        var migration = await db.XeroMigrations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganisationId == organisationId, cancellationToken);
        if (migration is null) return EmptyWorkspace();

        var records = await db.XeroMigrationRecords.AsNoTracking()
            .Where(x => x.XeroMigrationId == migration.Id).ToListAsync(cancellationToken);
        var mappings = await db.XeroMigrationMappings.AsNoTracking()
            .Where(x => x.XeroMigrationId == migration.Id)
            .OrderBy(x => x.Kind).ThenBy(x => x.SourceName).ToListAsync(cancellationToken);
        var issues = await db.XeroMigrationIssues.AsNoTracking()
            .Where(x => x.XeroMigrationId == migration.Id && !x.IsResolved)
            .OrderByDescending(x => x.Severity).ThenBy(x => x.Code).ToListAsync(cancellationToken);
        var runs = await db.XeroMigrationImportRuns.AsNoTracking()
            .Where(x => x.XeroMigrationId == migration.Id).ToListAsync(cancellationToken);
        var lastRun = runs.OrderByDescending(x => x.StartedAt).FirstOrDefault();

        return new(
            records.Select(x => x.DatasetKey).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            records.Count,
            records.Count(x => x.Status == XeroMigrationRecordStatus.Ready),
            records.Count(x => x.Status == XeroMigrationRecordStatus.NeedsMapping),
            records.Count(x => x.Status == XeroMigrationRecordStatus.Imported),
            issues.Count(x => x.Severity == XeroMigrationIssueSeverity.Warning),
            issues.Count(x => x.Severity == XeroMigrationIssueSeverity.Blocking),
            mappings, issues, lastRun);
    }

    public async Task<XeroImportPreparationResult> PrepareAsync(
        string userId, Guid organisationId, CancellationToken cancellationToken = default)
    {
        await DemandPrepareAsync(userId, organisationId);
        var migration = await db.XeroMigrations.SingleOrDefaultAsync(
            x => x.OrganisationId == organisationId, cancellationToken)
            ?? throw new InvalidOperationException("Save the migration setup first.");
        var latest = (await db.XeroApiSnapshots
                .Where(x => x.XeroMigrationId == migration.Id)
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.DatasetKey, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderByDescending(snapshot => snapshot.RetrievedAt).First()).ToList();
        if (latest.Count == 0)
            throw new InvalidOperationException("Copy data from Xero before preparing the import.");

        var existingRecords = await db.XeroMigrationRecords
            .Where(x => x.XeroMigrationId == migration.Id).ToListAsync(cancellationToken);
        var recordsByKey = existingRecords.ToDictionary(
            x => RecordKey(x.DatasetKey, x.ExternalId), StringComparer.OrdinalIgnoreCase);
        var existingMappings = await db.XeroMigrationMappings
            .Where(x => x.XeroMigrationId == migration.Id).ToListAsync(cancellationToken);
        var mappingsByKey = existingMappings.ToDictionary(
            x => MappingKey(x.Kind, x.SourceKey), StringComparer.OrdinalIgnoreCase);

        var currentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var snapshot in latest)
        {
            var bytes = await storage.ReadVerifiedAsync(organisationId, snapshot.ImmutableDocumentObjectId, cancellationToken);
            using var document = JsonDocument.Parse(bytes);
            var ordinal = 0;
            foreach (var source in EnumerateRecords(document.RootElement, snapshot.DatasetKey))
            {
                ordinal++;
                var externalId = ExternalId(snapshot.DatasetKey, source, ordinal);
                var key = RecordKey(snapshot.DatasetKey, externalId);
                currentKeys.Add(key);
                var hash = Hash(source.GetRawText());
                var display = DisplayName(snapshot.DatasetKey, source, externalId);
                var sourceDate = SourceDate(source);
                if (!recordsByKey.TryGetValue(key, out var record))
                {
                    record = new XeroMigrationRecord
                    {
                        OrganisationId = organisationId,
                        XeroMigrationId = migration.Id,
                        XeroApiSnapshotId = snapshot.Id,
                        DatasetKey = snapshot.DatasetKey,
                        ExternalId = externalId,
                        DisplayName = display,
                        SourceHash = hash,
                        SourceDate = sourceDate,
                        Status = InitialStatus(snapshot.DatasetKey),
                        PreparedByUserId = userId
                    };
                    db.XeroMigrationRecords.Add(record);
                    recordsByKey[key] = record;
                }
                else if (record.Status != XeroMigrationRecordStatus.Imported)
                {
                    record.XeroApiSnapshotId = snapshot.Id;
                    record.DisplayName = display;
                    record.SourceHash = hash;
                    record.SourceDate = sourceDate;
                    record.Status = InitialStatus(snapshot.DatasetKey);
                    record.ReviewMessage = null;
                    record.PreparedAt = DateTimeOffset.UtcNow;
                    record.PreparedByUserId = userId;
                }
                else if (!record.SourceHash.Equals(hash, StringComparison.Ordinal))
                {
                    record.ReviewMessage = "This Xero record changed after it was imported. Review it before cutover.";
                }

                AddMappings(migration, organisationId, userId, snapshot.DatasetKey, source, mappingsByKey);
            }
        }

        foreach (var stale in existingRecords.Where(x =>
                     x.Status != XeroMigrationRecordStatus.Imported &&
                     !currentKeys.Contains(RecordKey(x.DatasetKey, x.ExternalId))))
        {
            stale.Status = XeroMigrationRecordStatus.Skipped;
            stale.ReviewMessage = "This object is no longer present in the latest Xero snapshot.";
        }

        await db.SaveChangesAsync(cancellationToken);
        await AutoMatchAsync(userId, organisationId, migration.Id, cancellationToken);
        await RebuildIssuesAsync(userId, organisationId, migration.Id, cancellationToken);

        var workspace = await GetWorkspaceAsync(userId, organisationId, cancellationToken);
        var run = new XeroMigrationImportRun
        {
            OrganisationId = organisationId,
            XeroMigrationId = migration.Id,
            Status = workspace.BlockingCount > 0 ? XeroMigrationImportRunStatus.Blocked : XeroMigrationImportRunStatus.Ready,
            IsDryRun = true,
            SourceRecordCount = workspace.SourceRecordCount,
            ReadyCount = workspace.ReadyCount,
            ImportedCount = workspace.ImportedCount,
            WarningCount = workspace.WarningCount,
            BlockingCount = workspace.BlockingCount,
            Summary = workspace.BlockingCount > 0
                ? $"Resolve {workspace.BlockingCount} blocking issue(s) before importing."
                : "The staged data passed migration validation.",
            StartedByUserId = userId,
            CompletedAt = DateTimeOffset.UtcNow
        };
        db.XeroMigrationImportRuns.Add(run);
        db.AuditEvents.Add(Audit(organisationId, userId, "XeroMigrationPrepared", migration.Id, new
        {
            workspace.DatasetCount, workspace.SourceRecordCount, workspace.ReadyCount,
            workspace.NeedsMappingCount, workspace.WarningCount, workspace.BlockingCount
        }));
        await db.SaveChangesAsync(cancellationToken);
        return new(workspace.DatasetCount, workspace.SourceRecordCount, workspace.ReadyCount,
            workspace.NeedsMappingCount, workspace.WarningCount, workspace.BlockingCount);
    }

    /// <summary>
    /// Creates safe master records only. Financial transactions remain in staging
    /// until their account, tax, tracking and allocation dependencies are confirmed.
    /// </summary>
    public async Task<int> ImportMasterDataAsync(
        string userId, Guid organisationId, CancellationToken cancellationToken = default)
    {
        await DemandPrepareAsync(userId, organisationId);
        var migration = await db.XeroMigrations.SingleOrDefaultAsync(
            x => x.OrganisationId == organisationId, cancellationToken)
            ?? throw new InvalidOperationException("Save the migration setup first.");
        var snapshots = (await db.XeroApiSnapshots
                .Where(x => x.XeroMigrationId == migration.Id &&
                            new[] { "accounts", "contacts", "currencies", "items" }.Contains(x.DatasetKey))
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.DatasetKey).Select(x => x.OrderByDescending(snapshot => snapshot.RetrievedAt).First()).ToList();
        if (snapshots.Count == 0) throw new InvalidOperationException("Prepare Xero data first.");

        var imported = 0;
        foreach (var snapshot in snapshots)
        {
            var bytes = await storage.ReadVerifiedAsync(organisationId, snapshot.ImmutableDocumentObjectId, cancellationToken);
            using var document = JsonDocument.Parse(bytes);
            var ordinal = 0;
            foreach (var source in EnumerateRecords(document.RootElement, snapshot.DatasetKey))
            {
                ordinal++;
                var externalId = ExternalId(snapshot.DatasetKey, source, ordinal);
                var record = await db.XeroMigrationRecords.SingleOrDefaultAsync(x =>
                    x.XeroMigrationId == migration.Id && x.DatasetKey == snapshot.DatasetKey &&
                    x.ExternalId == externalId, cancellationToken);
                if (record is null || record.Status == XeroMigrationRecordStatus.Imported) continue;
                Guid? targetId = snapshot.DatasetKey switch
                {
                    "accounts" => await ImportAccountAsync(organisationId, source, cancellationToken),
                    "contacts" => await ImportContactAsync(organisationId, source, cancellationToken),
                    "currencies" => await ImportCurrencyAsync(userId, organisationId, source, cancellationToken),
                    "items" => await ImportItemAsync(organisationId, source, cancellationToken),
                    _ => null
                };
                if (targetId is null) continue;
                record.Status = XeroMigrationRecordStatus.Imported;
                record.TargetEntityType = TargetType(snapshot.DatasetKey);
                record.TargetEntityId = targetId;
                record.ImportedAt = DateTimeOffset.UtcNow;
                record.ImportedByUserId = userId;
                imported++;
            }
        }
        db.AuditEvents.Add(Audit(organisationId, userId, "XeroMasterDataImported", migration.Id, new { Imported = imported }));
        await db.SaveChangesAsync(cancellationToken);
        await AutoMatchAsync(userId, organisationId, migration.Id, cancellationToken);
        await RebuildIssuesAsync(userId, organisationId, migration.Id, cancellationToken);
        return imported;
    }

    public async Task ConfirmMappingAsync(
        string userId, Guid organisationId, Guid mappingId, Guid? targetId,
        string? targetDisplayName, bool ignored, CancellationToken cancellationToken = default)
    {
        await DemandPrepareAsync(userId, organisationId);
        var mapping = await db.XeroMigrationMappings.SingleOrDefaultAsync(
            x => x.Id == mappingId && x.OrganisationId == organisationId, cancellationToken)
            ?? throw new InvalidOperationException("The migration mapping was not found.");
        mapping.TargetEntityId = targetId;
        mapping.TargetDisplayName = string.IsNullOrWhiteSpace(targetDisplayName) ? null : targetDisplayName.Trim();
        mapping.Status = ignored ? XeroMigrationMappingStatus.Ignored : XeroMigrationMappingStatus.Confirmed;
        mapping.UpdatedAt = DateTimeOffset.UtcNow;
        mapping.UpdatedByUserId = userId;
        db.AuditEvents.Add(Audit(organisationId, userId, "XeroMigrationMappingConfirmed", mapping.Id, new
        {
            mapping.Kind, mapping.SourceKey, mapping.TargetEntityType, mapping.TargetEntityId,
            mapping.TargetDisplayName, mapping.Status
        }));
        await db.SaveChangesAsync(cancellationToken);
        await RebuildIssuesAsync(userId, organisationId, mapping.XeroMigrationId, cancellationToken);
    }

    public async Task<IReadOnlyList<XeroMappingTarget>> GetMappingTargetsAsync(
        string userId, Guid organisationId, XeroMigrationMappingKind kind,
        CancellationToken cancellationToken = default)
    {
        await DemandAccessAsync(userId, organisationId);
        return kind switch
        {
            XeroMigrationMappingKind.Account or XeroMigrationMappingKind.AssetType =>
                await db.LedgerAccounts.AsNoTracking().Where(x => x.OrganisationId == organisationId && x.IsActive)
                    .OrderBy(x => x.Code).Select(x => new XeroMappingTarget(x.Id, x.Id.ToString(), x.Code + " · " + x.Name, nameof(LedgerAccount)))
                    .ToListAsync(cancellationToken),
            XeroMigrationMappingKind.Contact =>
                await db.BusinessParties.AsNoTracking().Where(x => x.OrganisationId == organisationId && x.IsActive)
                    .OrderBy(x => x.Name).Select(x => new XeroMappingTarget(x.Id, x.Id.ToString(), x.Name, nameof(BusinessParty)))
                    .ToListAsync(cancellationToken),
            XeroMigrationMappingKind.Item =>
                await db.ProductItems.AsNoTracking().Where(x => x.OrganisationId == organisationId && x.IsActive)
                    .OrderBy(x => x.Code).Select(x => new XeroMappingTarget(x.Id, x.Id.ToString(), x.Code + " · " + x.Name, nameof(ProductItem)))
                    .ToListAsync(cancellationToken),
            XeroMigrationMappingKind.Currency =>
                await db.OrganisationCurrencies.AsNoTracking().Where(x => x.OrganisationId == organisationId && x.IsActive)
                    .OrderBy(x => x.Code).Select(x => new XeroMappingTarget(x.Id, x.Id.ToString(), x.Code + " · " + x.Name, nameof(OrganisationCurrency)))
                    .ToListAsync(cancellationToken),
            XeroMigrationMappingKind.TrackingCategory or XeroMigrationMappingKind.TrackingOption =>
                (await db.Branches.AsNoTracking().Where(x => x.OrganisationId == organisationId && x.IsActive)
                    .OrderBy(x => x.Code).Select(x => new XeroMappingTarget(x.Id, x.Id.ToString(), "Branch · " + x.Code + " · " + x.Name, nameof(Branch)))
                    .ToListAsync(cancellationToken))
                .Concat(await db.Divisions.AsNoTracking().Where(x => x.Branch.OrganisationId == organisationId && x.IsActive)
                    .OrderBy(x => x.Code).Select(x => new XeroMappingTarget(x.Id, x.Id.ToString(), "Division · " + x.Code + " · " + x.Name, nameof(Division)))
                    .ToListAsync(cancellationToken)).ToList(),
            XeroMigrationMappingKind.TaxRate => Enum.GetValues<VatTreatment>()
                .Select(x => new XeroMappingTarget(null, x.ToString(), x.ToString(), nameof(VatTreatment))).ToList(),
            _ => []
        };
    }

    private async Task AutoMatchAsync(string userId, Guid organisationId, Guid migrationId, CancellationToken ct)
    {
        var mappings = await db.XeroMigrationMappings.Where(x =>
            x.XeroMigrationId == migrationId && x.Status == XeroMigrationMappingStatus.Suggested).ToListAsync(ct);
        var accounts = await db.LedgerAccounts.Where(x => x.OrganisationId == organisationId).ToListAsync(ct);
        var contacts = await db.BusinessParties.Where(x => x.OrganisationId == organisationId).ToListAsync(ct);
        var items = await db.ProductItems.Where(x => x.OrganisationId == organisationId).ToListAsync(ct);
        var currencies = await db.OrganisationCurrencies.Where(x => x.OrganisationId == organisationId).ToListAsync(ct);
        foreach (var mapping in mappings)
        {
            (Guid? id, string? name, string? type) match = mapping.Kind switch
            {
                XeroMigrationMappingKind.Account => Match(accounts, x => x.Id, x => x.Code, x => x.Name, mapping),
                XeroMigrationMappingKind.Contact => Match(contacts, x => x.Id, _ => "", x => x.Name, mapping),
                XeroMigrationMappingKind.Item => Match(items, x => x.Id, x => x.Code, x => x.Name, mapping),
                XeroMigrationMappingKind.Currency => Match(currencies, x => x.Id, x => x.Code, x => x.Name, mapping),
                XeroMigrationMappingKind.TaxRate => TaxMatch(mapping),
                _ => (null, null, null)
            };
            if (match.id is not null || match.name is not null)
            {
                mapping.TargetEntityId = match.id;
                mapping.TargetDisplayName = match.name;
                mapping.TargetEntityType = match.type ?? mapping.TargetEntityType;
                mapping.Status = XeroMigrationMappingStatus.Confirmed;
                mapping.UpdatedAt = DateTimeOffset.UtcNow;
                mapping.UpdatedByUserId = userId;
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task RebuildIssuesAsync(string userId, Guid organisationId, Guid migrationId, CancellationToken ct)
    {
        var old = await db.XeroMigrationIssues.Where(x => x.XeroMigrationId == migrationId && !x.IsResolved).ToListAsync(ct);
        foreach (var issue in old)
        {
            issue.IsResolved = true;
            issue.ResolvedAt = DateTimeOffset.UtcNow;
            issue.ResolvedByUserId = userId;
            issue.Resolution = "Replaced by the latest validation run.";
        }
        var mappings = await db.XeroMigrationMappings.AsNoTracking()
            .Where(x => x.XeroMigrationId == migrationId).ToListAsync(ct);
        foreach (var mapping in mappings.Where(x => x.Status == XeroMigrationMappingStatus.Suggested))
        {
            db.XeroMigrationIssues.Add(Issue(organisationId, migrationId, "MAPPING_REQUIRED",
                XeroMigrationIssueSeverity.Blocking,
                $"Choose how Xero {mapping.Kind.ToString().ToLowerInvariant()} ‘{mapping.SourceName}’ maps into Account Island."));
        }
        foreach (var mapping in mappings.Where(x => x.Status == XeroMigrationMappingStatus.Ignored))
        {
            db.XeroMigrationIssues.Add(Issue(organisationId, migrationId, "MAPPING_IGNORED",
                XeroMigrationIssueSeverity.Warning,
                $"Xero {mapping.Kind.ToString().ToLowerInvariant()} ‘{mapping.SourceName}’ will be excluded."));
        }
        var records = await db.XeroMigrationRecords.Where(x => x.XeroMigrationId == migrationId).ToListAsync(ct);
        foreach (var changed in records.Where(x => x.Status == XeroMigrationRecordStatus.Imported && x.ReviewMessage != null))
        {
            db.XeroMigrationIssues.Add(Issue(organisationId, migrationId, "SOURCE_CHANGED",
                XeroMigrationIssueSeverity.Blocking, changed.ReviewMessage!, changed.Id));
        }
        if (!records.Any(x => x.DatasetKey == "manual-journals"))
            db.XeroMigrationIssues.Add(Issue(organisationId, migrationId, "JOURNALS_MISSING",
                XeroMigrationIssueSeverity.Blocking, "No Xero manual journals were found in the latest API copy."));
        if (!records.Any(x => x.DatasetKey == "accounts"))
            db.XeroMigrationIssues.Add(Issue(organisationId, migrationId, "ACCOUNTS_MISSING",
                XeroMigrationIssueSeverity.Blocking, "The Xero chart of accounts is missing."));
        foreach (var pending in records
                     .Where(x => LiveImportDatasets.ContainsKey(x.DatasetKey) &&
                                 x.Status is not XeroMigrationRecordStatus.Imported and
                                     not XeroMigrationRecordStatus.Skipped)
                     .GroupBy(x => x.DatasetKey, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x.Key))
        {
            var label = LiveImportDatasets[pending.Key];
            db.XeroMigrationIssues.Add(Issue(organisationId, migrationId, "LIVE_IMPORT_PENDING",
                XeroMigrationIssueSeverity.Blocking,
                $"{pending.Count():N0} Xero {label} still need to be converted into Account Island records."));
        }
        db.XeroMigrationIssues.Add(Issue(organisationId, migrationId, "ATTACHMENTS_MANUAL",
            XeroMigrationIssueSeverity.Warning,
            "Download and retain Xero document attachments before final cutover; the accounting API copy contains their parent records only."));
        await db.SaveChangesAsync(ct);
    }

    private void AddMappings(
        XeroMigration migration, Guid organisationId, string userId, string dataset,
        JsonElement source, IDictionary<string, XeroMigrationMapping> mappings)
    {
        void Add(XeroMigrationMappingKind kind, string sourceKey, string sourceName, string targetType)
        {
            if (string.IsNullOrWhiteSpace(sourceKey)) return;
            var key = MappingKey(kind, sourceKey);
            if (mappings.ContainsKey(key)) return;
            var mapping = new XeroMigrationMapping
            {
                OrganisationId = organisationId, XeroMigrationId = migration.Id,
                Kind = kind, SourceKey = sourceKey, SourceName = sourceName,
                TargetEntityType = targetType, Status = XeroMigrationMappingStatus.Suggested,
                UpdatedByUserId = userId
            };
            mappings[key] = mapping;
            db.XeroMigrationMappings.Add(mapping);
        }

        if (dataset.Equals("accounts", StringComparison.OrdinalIgnoreCase))
            Add(XeroMigrationMappingKind.Account, Text(source, "Code", "AccountID"),
                Join(source, "Code", "Name"), nameof(LedgerAccount));
        if (dataset.Equals("tax-rates", StringComparison.OrdinalIgnoreCase))
            Add(XeroMigrationMappingKind.TaxRate, Text(source, "TaxType", "Name"),
                Join(source, "TaxType", "Name"), nameof(VatTreatment));
        if (dataset.Equals("contacts", StringComparison.OrdinalIgnoreCase))
            Add(XeroMigrationMappingKind.Contact, Text(source, "ContactID", "Name"),
                Text(source, "Name", "ContactID"), nameof(BusinessParty));
        if (dataset.Equals("currencies", StringComparison.OrdinalIgnoreCase))
            Add(XeroMigrationMappingKind.Currency, Text(source, "Code"),
                Join(source, "Code", "Description"), nameof(OrganisationCurrency));
        if (dataset.Equals("items", StringComparison.OrdinalIgnoreCase))
            Add(XeroMigrationMappingKind.Item, Text(source, "Code", "ItemID"),
                Join(source, "Code", "Name"), nameof(ProductItem));
        if (dataset.Equals("asset-types", StringComparison.OrdinalIgnoreCase))
            Add(XeroMigrationMappingKind.AssetType, Text(source, "AssetTypeID", "AssetTypeName"),
                Text(source, "AssetTypeName", "AssetTypeID"), nameof(FixedAsset));
        if (dataset.Equals("tracking", StringComparison.OrdinalIgnoreCase))
        {
            var categoryId = Text(source, "TrackingCategoryID", "Name");
            Add(XeroMigrationMappingKind.TrackingCategory, categoryId,
                Text(source, "Name", "TrackingCategoryID"), "Branch or division");
            if (TryProperty(source, "Options", out var options) && options.ValueKind == JsonValueKind.Array)
                foreach (var option in options.EnumerateArray())
                    Add(XeroMigrationMappingKind.TrackingOption,
                        $"{categoryId}:{Text(option, "TrackingOptionID", "Name")}",
                        $"{Text(source, "Name")} · {Text(option, "Name")}", "Branch or division");
        }
    }

    private async Task<Guid?> ImportAccountAsync(Guid organisationId, JsonElement source, CancellationToken ct)
    {
        var code = Text(source, "Code");
        var name = Text(source, "Name");
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name)) return null;
        var account = await db.LedgerAccounts.SingleOrDefaultAsync(
            x => x.OrganisationId == organisationId && x.Code == code, ct);
        if (account is null)
        {
            var xeroType = Text(source, "Type");
            account = new LedgerAccount
            {
                OrganisationId = organisationId, Code = code[..Math.Min(code.Length, 20)],
                Name = name[..Math.Min(name.Length, 160)], Type = AccountTypeFor(xeroType),
                IsBankAccount = xeroType is "BANK" or "CURRENT" or "CREDITCARD",
                BankAccountKind = xeroType == "CREDITCARD" ? BankAccountKind.CreditCard : BankAccountKind.Bank,
                BankAccountNumber = NullIfBlank(Text(source, "BankAccountNumber")),
                IsActive = !Text(source, "Status").Equals("ARCHIVED", StringComparison.OrdinalIgnoreCase)
            };
            db.LedgerAccounts.Add(account);
        }
        return account.Id;
    }

    private async Task<Guid?> ImportContactAsync(Guid organisationId, JsonElement source, CancellationToken ct)
    {
        var name = Text(source, "Name");
        if (string.IsNullOrWhiteSpace(name)) return null;
        var email = NullIfBlank(Text(source, "EmailAddress"));
        var contact = await db.BusinessParties.FirstOrDefaultAsync(x =>
            x.OrganisationId == organisationId &&
            (x.Name == name || (email != null && x.Email == email)), ct);
        var isCustomer = Bool(source, "IsCustomer");
        var isSupplier = Bool(source, "IsSupplier");
        var type = (isCustomer ? PartyType.Customer : 0) | (isSupplier ? PartyType.Supplier : 0);
        if (type == 0) type = PartyType.Customer | PartyType.Supplier;
        if (contact is null)
        {
            contact = new BusinessParty
            {
                OrganisationId = organisationId, Name = name[..Math.Min(name.Length, 160)],
                Email = email, AccountsEmail = email, Phone = FirstPhone(source),
                Address = FirstAddress(source), Tin = NullIfBlank(Text(source, "TaxNumber")),
                Type = type, IsActive = !Text(source, "ContactStatus").Equals("ARCHIVED", StringComparison.OrdinalIgnoreCase),
                DefaultSalesCurrency = NullIfBlank(Text(source, "DefaultCurrency")),
                DefaultPurchaseCurrency = NullIfBlank(Text(source, "DefaultCurrency"))
            };
            db.BusinessParties.Add(contact);
        }
        else contact.Type |= type;
        return contact.Id;
    }

    private async Task<Guid?> ImportCurrencyAsync(string userId, Guid organisationId, JsonElement source, CancellationToken ct)
    {
        var code = Text(source, "Code");
        if (code.Length != 3) return null;
        var currency = await db.OrganisationCurrencies.SingleOrDefaultAsync(
            x => x.OrganisationId == organisationId && x.Code == code, ct);
        if (currency is null)
        {
            currency = new OrganisationCurrency
            {
                OrganisationId = organisationId, Code = code.ToUpperInvariant(),
                Name = NullIfBlank(Text(source, "Description")) ?? code.ToUpperInvariant(),
                CreatedByUserId = userId
            };
            db.OrganisationCurrencies.Add(currency);
        }
        return currency.Id;
    }

    private async Task<Guid?> ImportItemAsync(Guid organisationId, JsonElement source, CancellationToken ct)
    {
        var code = Text(source, "Code");
        if (string.IsNullOrWhiteSpace(code)) return null;
        var item = await db.ProductItems.SingleOrDefaultAsync(
            x => x.OrganisationId == organisationId && x.Code == code, ct);
        if (item is null)
        {
            item = new ProductItem
            {
                OrganisationId = organisationId, Code = code[..Math.Min(code.Length, 40)],
                Name = (NullIfBlank(Text(source, "Name")) ?? code)[..Math.Min((NullIfBlank(Text(source, "Name")) ?? code).Length, 160)],
                Description = NullIfBlank(Text(source, "Description")),
                Kind = Bool(source, "IsTrackedAsInventory") ? ProductKind.TrackedItem : ProductKind.NonTrackedItem,
                SalePrice = NestedDecimal(source, "SalesDetails", "UnitPrice"),
                PurchasePrice = NestedDecimal(source, "PurchaseDetails", "UnitPrice"),
                IsActive = !Text(source, "Status").Equals("ARCHIVED", StringComparison.OrdinalIgnoreCase)
            };
            db.ProductItems.Add(item);
        }
        return item.Id;
    }

    private static IEnumerable<JsonElement> EnumerateRecords(JsonElement wrapper, string dataset)
    {
        if (!TryProperty(wrapper, "pages", out var pages) || pages.ValueKind != JsonValueKind.Array) yield break;
        var rootName = RootProperties.GetValueOrDefault(dataset);
        foreach (var page in pages.EnumerateArray())
        {
            if (page.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in page.EnumerateArray()) yield return item;
                continue;
            }
            if (rootName is not null && TryProperty(page, rootName, out var records) && records.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in records.EnumerateArray()) yield return item;
                continue;
            }
            if (page.ValueKind == JsonValueKind.Object) yield return page;
        }
    }

    private static string ExternalId(string dataset, JsonElement source, int ordinal)
    {
        var candidates = dataset switch
        {
            "organisation" => new[] { "OrganisationID", "Name" }, "accounts" => new[] { "AccountID", "Code" },
            "tax-rates" => new[] { "TaxType", "Name" }, "currencies" => new[] { "Code" },
            "tracking" => new[] { "TrackingCategoryID", "Name" }, "contacts" => new[] { "ContactID", "Name" },
            "items" => new[] { "ItemID", "Code" }, "sales-invoices" or "bills" => new[] { "InvoiceID", "InvoiceNumber" },
            "sales-credits" or "supplier-credits" => new[] { "CreditNoteID", "CreditNoteNumber" },
            "payments" => new[] { "PaymentID" }, "batch-payments" => new[] { "BatchPaymentID" },
            "prepayments" => new[] { "PrepaymentID" }, "overpayments" => new[] { "OverpaymentID" },
            "bank-transactions" => new[] { "BankTransactionID" }, "bank-transfers" => new[] { "BankTransferID" },
            "manual-journals" => new[] { "ManualJournalID", "Narration" },
            "purchase-orders" => new[] { "PurchaseOrderID", "PurchaseOrderNumber" },
            "quotes" => new[] { "QuoteID", "QuoteNumber" }, "repeating-invoices" => new[] { "RepeatingInvoiceID" },
            "budgets" => new[] { "BudgetID", "Description" }, "assets" => new[] { "AssetId", "AssetID", "AssetNumber" },
            "asset-types" => new[] { "AssetTypeID", "AssetTypeName" },
            _ => new[] { "ReportID", "ReportName", "Name" }
        };
        var value = Text(source, candidates);
        return string.IsNullOrWhiteSpace(value) ? $"{dataset}:{ordinal}:{Hash(source.GetRawText())[..12]}" : value;
    }

    private static string DisplayName(string dataset, JsonElement source, string fallback)
    {
        var value = Text(source, "InvoiceNumber", "CreditNoteNumber", "PurchaseOrderNumber", "QuoteNumber",
            "Code", "Name", "Narration", "Reference", "AssetName", "ReportName");
        return (string.IsNullOrWhiteSpace(value) ? fallback : value)[..Math.Min(string.IsNullOrWhiteSpace(value) ? fallback.Length : value.Length, 240)];
    }

    private static XeroMigrationRecordStatus InitialStatus(string dataset) =>
        ControlDatasets.Contains(dataset) ? XeroMigrationRecordStatus.NeedsReview : XeroMigrationRecordStatus.Ready;

    private static string? TargetType(string dataset) => dataset switch
    {
        "accounts" => nameof(LedgerAccount), "contacts" => nameof(BusinessParty),
        "currencies" => nameof(OrganisationCurrency), "items" => nameof(ProductItem), _ => null
    };

    private static AccountType AccountTypeFor(string xeroType) => xeroType.ToUpperInvariant() switch
    {
        "EQUITY" => AccountType.Equity,
        "REVENUE" or "SALES" or "OTHERINCOME" => AccountType.Revenue,
        "EXPENSE" or "DIRECTCOSTS" or "OVERHEADS" or "DEPRECIATN" => AccountType.Expense,
        "LIABILITY" or "CURRLIAB" or "TERMLIAB" or "PAYGLIABILITY" => AccountType.Liability,
        _ => AccountType.Asset
    };

    private static (Guid?, string?, string?) Match<T>(IEnumerable<T> values, Func<T, Guid> id,
        Func<T, string> code, Func<T, string> name, XeroMigrationMapping mapping)
    {
        var match = values.FirstOrDefault(x =>
            code(x).Equals(mapping.SourceKey, StringComparison.OrdinalIgnoreCase) ||
            code(x).Equals(mapping.SourceName.Split('·')[0].Trim(), StringComparison.OrdinalIgnoreCase) ||
            name(x).Equals(mapping.SourceName, StringComparison.OrdinalIgnoreCase) ||
            mapping.SourceName.EndsWith(name(x), StringComparison.OrdinalIgnoreCase));
        return match is null ? (null, null, null) : (id(match), $"{code(match)} {name(match)}".Trim(), typeof(T).Name);
    }

    private static (Guid?, string?, string?) TaxMatch(XeroMigrationMapping mapping)
    {
        var text = $"{mapping.SourceKey} {mapping.SourceName}".ToUpperInvariant();
        var target = text.Contains("EXEMPT") ? nameof(VatTreatment.Exempt)
            : text.Contains("ZERO") ? nameof(VatTreatment.ZeroRated)
            : text.Contains("OUTPUT") || text.Contains("INPUT") || text.Contains("GST") ? nameof(VatTreatment.Standard)
            : text.Contains("NONE") || text.Contains("EXCLUDED") || text.Contains("OUT OF SCOPE") ? nameof(VatTreatment.OutOfScope)
            : null;
        return (null, target, nameof(VatTreatment));
    }

    private static XeroMigrationIssue Issue(Guid organisationId, Guid migrationId, string code,
        XeroMigrationIssueSeverity severity, string message, Guid? recordId = null) => new()
    {
        OrganisationId = organisationId, XeroMigrationId = migrationId, XeroMigrationRecordId = recordId,
        Code = code, Severity = severity, Message = message
    };

    private static XeroImportWorkspace EmptyWorkspace() => new(0, 0, 0, 0, 0, 0, 0, [], [], null);
    private static string RecordKey(string dataset, string externalId) => $"{dataset}\u001f{externalId}";
    private static string MappingKey(XeroMigrationMappingKind kind, string sourceKey) => $"{kind}\u001f{sourceKey}";
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                { value = property.Value; return true; }
        value = default; return false;
    }

    private static string Text(JsonElement element, params string[] names)
    {
        foreach (var name in names)
            if (TryProperty(element, name, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
        return string.Empty;
    }

    private static string Join(JsonElement element, params string[] names) =>
        string.Join(" · ", names.Select(x => Text(element, x)).Where(x => !string.IsNullOrWhiteSpace(x)));
    private static bool Bool(JsonElement element, string name) =>
        TryProperty(element, name, out var value) && (value.ValueKind == JsonValueKind.True ||
        value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) && parsed);
    private static decimal NestedDecimal(JsonElement element, string parent, string child) =>
        TryProperty(element, parent, out var nested) && TryProperty(nested, child, out var value) && value.TryGetDecimal(out var number) ? number : 0m;
    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static DateOnly? SourceDate(JsonElement source)
    {
        var value = Text(source, "Date", "UpdatedDateUTC", "InvoiceDate", "PurchaseOrderDate", "QuoteDate", "AcquisitionDate");
        if (DateOnly.TryParse(value, out var date)) return date;
        if (DateTimeOffset.TryParse(value, out var timestamp)) return DateOnly.FromDateTime(timestamp.DateTime);
        if (value.StartsWith("/Date(", StringComparison.Ordinal) && long.TryParse(value[6..].Split('+', '-', ')')[0], out var milliseconds))
            return DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).DateTime);
        return null;
    }
    private static string? FirstPhone(JsonElement source)
    {
        if (!TryProperty(source, "Phones", out var values) || values.ValueKind != JsonValueKind.Array) return null;
        foreach (var value in values.EnumerateArray())
        {
            var number = Text(value, "PhoneNumber");
            if (!string.IsNullOrWhiteSpace(number)) return number[..Math.Min(number.Length, 40)];
        }
        return null;
    }
    private static string? FirstAddress(JsonElement source)
    {
        if (!TryProperty(source, "Addresses", out var values) || values.ValueKind != JsonValueKind.Array) return null;
        foreach (var value in values.EnumerateArray())
        {
            var parts = new[] { "AddressLine1", "AddressLine2", "AddressLine3", "AddressLine4", "City", "Region", "PostalCode", "Country" }
                .Select(x => Text(value, x)).Where(x => !string.IsNullOrWhiteSpace(x));
            var address = string.Join(", ", parts);
            if (!string.IsNullOrWhiteSpace(address)) return address[..Math.Min(address.Length, 500)];
        }
        return null;
    }

    private async Task DemandAccessAsync(string userId, Guid organisationId)
    {
        if (await access.FindAsync(userId, organisationId) is null) throw new UnauthorizedAccessException();
    }
    private async Task DemandPrepareAsync(string userId, Guid organisationId)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId))
            throw new UnauthorizedAccessException("You cannot prepare this migration.");
    }
    private static AuditEvent Audit(Guid organisationId, string userId, string eventType, Guid entityId, object evidence) => new()
    {
        OrganisationId = organisationId, UserId = userId, EventType = eventType,
        EntityType = nameof(XeroMigration), EntityId = entityId.ToString(), JsonData = JsonSerializer.Serialize(evidence, JsonOptions)
    };
}
