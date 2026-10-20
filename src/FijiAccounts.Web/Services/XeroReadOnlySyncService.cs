using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FijiAccounts.Web.Services;

public sealed record XeroMirrorDatasetStatus(string Key, string Name, int ActiveRecords, int ArchivedRecords);

public sealed record XeroReadOnlySyncStatus(
    XeroConnectionStatus Connection,
    IReadOnlyList<XeroMirrorDatasetStatus> Datasets,
    IReadOnlyList<XeroReadOnlySyncRun> RecentRuns);

public sealed record XeroReadOnlySyncResult(
    bool FullReconciliation,
    int DatasetCount,
    int RecordCount,
    int ChangedCount,
    int ArchivedCount,
    IReadOnlyList<XeroSyncFailure> Failures);

/// <summary>
/// Maintains a one-way, read-only projection of Xero inside Account Island.
/// Xero owns all source values. Only safe master data is projected into normal
/// Account Island tables; financial transactions stay in the read-only mirror.
/// </summary>
public sealed class XeroReadOnlySyncService(
    ApplicationDbContext db,
    TenantAccessService access,
    XeroApiClient api,
    XeroConnectionService connections,
    IOptions<XeroApiOptions> configured,
    ILogger<XeroReadOnlySyncService> logger)
{
    private const string SystemActor = "system:xero-read-only-sync";
    private static readonly TimeSpan SyncLeaseDuration = TimeSpan.FromMinutes(30);
    private const int MaximumPages = 100;
    private const int ExpectedPageSize = 100;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly XeroApiOptions options = configured.Value;

    private static readonly DatasetDefinition[] Datasets =
    [
        new("accounts", "Chart of accounts", "Accounts", "Accounts", false, false),
        new("tax-rates", "Tax rates", "TaxRates", "TaxRates", false, false),
        new("currencies", "Currencies", "Currencies", "Currencies", false, false),
        new("tracking", "Tracking categories", "TrackingCategories", "TrackingCategories", false, false),
        new("contacts", "Contacts", "Contacts", "Contacts", true, true),
        new("items", "Items", "Items", "Items", true, true),
        new("sales-invoices", "Sales invoices", "Invoices?where=Type%3D%3D%22ACCREC%22", "Invoices", true, true),
        new("bills", "Supplier bills", "Invoices?where=Type%3D%3D%22ACCPAY%22", "Invoices", true, true),
        new("sales-credits", "Sales credit notes", "CreditNotes?where=Type%3D%3D%22ACCRECCREDIT%22", "CreditNotes", true, true),
        new("supplier-credits", "Supplier credit notes", "CreditNotes?where=Type%3D%3D%22ACCPAYCREDIT%22", "CreditNotes", true, true),
        new("payments", "Payments", "Payments", "Payments", true, true),
        new("prepayments", "Prepayments", "Prepayments", "Prepayments", true, true),
        new("overpayments", "Overpayments", "Overpayments", "Overpayments", true, true),
        new("bank-transactions", "Bank transactions", "BankTransactions", "BankTransactions", true, true),
        new("bank-transfers", "Bank transfers", "BankTransfers", "BankTransfers", false, true),
        new("manual-journals", "Manual journals", "ManualJournals", "ManualJournals", true, true)
    ];

    public async Task<XeroReadOnlySyncStatus> GetStatusAsync(
        string userId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        if (await access.FindAsync(userId, organisationId) is null) throw new UnauthorizedAccessException();
        var connection = await connections.GetStatusAsync(userId, organisationId, cancellationToken);
        var counts = await db.XeroMirrorRecords.AsNoTracking()
            .Where(x => x.OrganisationId == organisationId)
            .GroupBy(x => new { x.DatasetKey, x.IsArchived })
            .Select(x => new { x.Key.DatasetKey, x.Key.IsArchived, Count = x.Count() })
            .ToListAsync(cancellationToken);
        var datasets = Datasets.Select(definition => new XeroMirrorDatasetStatus(
                definition.Key,
                definition.Name,
                counts.Where(x => x.DatasetKey == definition.Key && !x.IsArchived).Sum(x => x.Count),
                counts.Where(x => x.DatasetKey == definition.Key && x.IsArchived).Sum(x => x.Count)))
            .Where(x => x.ActiveRecords > 0 || x.ArchivedRecords > 0)
            .ToList();
        // SQLite cannot translate ORDER BY for DateTimeOffset. Materialise this
        // organisation's small run history first, then order it in memory.
        var recentRuns = (await db.XeroReadOnlySyncRuns.AsNoTracking()
            .Where(x => x.OrganisationId == organisationId)
            .ToListAsync(cancellationToken))
            .OrderByDescending(x => x.StartedAt)
            .Take(10)
            .ToList();
        return new(connection, datasets, recentRuns);
    }

    public async Task SetEnabledAsync(
        string userId,
        Guid organisationId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId))
            throw new UnauthorizedAccessException("Only an owner or administrator can manage Xero synchronisation.");
        var connection = await db.XeroConnections.SingleOrDefaultAsync(
            x => x.OrganisationId == organisationId && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("Connect Xero first.");
        if (connection.XeroTenantId is null) throw new InvalidOperationException("Choose the Xero organisation first.");
        connection.ContinuousSyncEnabled = enabled;
        connection.NextSyncAt = enabled ? DateTimeOffset.UtcNow : null;
        connection.UpdatedAt = DateTimeOffset.UtcNow;
        connection.UpdatedByUserId = userId;
        db.AuditEvents.Add(Audit(organisationId, userId,
            enabled ? "XeroReadOnlySyncEnabled" : "XeroReadOnlySyncDisabled", connection.Id,
            new { connection.XeroTenantId, connection.XeroTenantName }));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<XeroReadOnlySyncResult> RunNowAsync(
        string userId,
        Guid organisationId,
        bool forceFullReconciliation = false,
        CancellationToken cancellationToken = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId))
            throw new UnauthorizedAccessException("Only an owner or administrator can refresh Xero data.");
        return await RunCoreAsync(organisationId, userId, "manual", forceFullReconciliation, true, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetDueOrganisationIdsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var candidates = await db.XeroConnections.AsNoTracking()
            .Where(x => x.IsActive && x.ContinuousSyncEnabled && x.XeroTenantId != null)
            .Select(x => new { x.OrganisationId, x.NextSyncAt, x.SyncLeaseUntil })
            .ToListAsync(cancellationToken);
        return candidates
            .Where(x => (x.NextSyncAt is null || x.NextSyncAt <= now) &&
                        (x.SyncLeaseUntil is null || x.SyncLeaseUntil <= now))
            .OrderBy(x => x.NextSyncAt)
            .Select(x => x.OrganisationId)
            .ToList();
    }

    public async Task<XeroReadOnlySyncResult> RunDueAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default) =>
        await RunCoreAsync(organisationId, SystemActor, "scheduled", false, false, cancellationToken);

    public async Task RequestSyncForTenantsAsync(
        IReadOnlyCollection<Guid> tenantIds,
        CancellationToken cancellationToken = default)
    {
        if (tenantIds.Count == 0) return;
        await db.XeroConnections
            .Where(x => x.IsActive && x.ContinuousSyncEnabled && x.XeroTenantId != null && tenantIds.Contains(x.XeroTenantId.Value))
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.NextSyncAt, DateTimeOffset.UtcNow), cancellationToken);
    }

    private async Task<XeroReadOnlySyncResult> RunCoreAsync(
        Guid organisationId,
        string actor,
        string trigger,
        bool forceFullReconciliation,
        bool allowDisabled,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var leaseCandidate = await db.XeroConnections.AsNoTracking()
            .Where(x => x.OrganisationId == organisationId && x.IsActive && x.XeroTenantId != null &&
                        (allowDisabled || x.ContinuousSyncEnabled))
            .Select(x => new { x.Id, x.SyncLeaseUntil })
            .SingleOrDefaultAsync(cancellationToken);
        if (leaseCandidate is null || leaseCandidate.SyncLeaseUntil > now)
            throw new InvalidOperationException("A Xero refresh is already running or the connection is not ready.");

        var leaseUntil = now.Add(SyncLeaseDuration);
        var claimed = await db.XeroConnections
            .Where(x => x.Id == leaseCandidate.Id && x.SyncLeaseUntil == leaseCandidate.SyncLeaseUntil)
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.SyncLeaseUntil, leaseUntil), cancellationToken);
        if (claimed == 0) throw new InvalidOperationException("A Xero refresh is already running or the connection is not ready.");

        var connection = await db.XeroConnections.SingleAsync(
            x => x.OrganisationId == organisationId && x.IsActive, cancellationToken);
        connection.SyncLeaseUntil = leaseUntil;
        var interruptedRuns = await db.XeroReadOnlySyncRuns
            .Where(x => x.OrganisationId == organisationId && x.Status == XeroReadOnlySyncRunStatus.Running)
            .ToListAsync(cancellationToken);
        foreach (var interruptedRun in interruptedRuns)
        {
            interruptedRun.Status = XeroReadOnlySyncRunStatus.Failed;
            interruptedRun.ErrorSummary = "The previous Xero refresh was interrupted before it could finish. Account Island resumed it automatically.";
            interruptedRun.CompletedAt = now;
        }
        var full = forceFullReconciliation || connection.LastSyncedAt is null ||
                   connection.LastFullReconciliationAt is null ||
                   connection.LastFullReconciliationAt <= now.AddHours(-Math.Clamp(options.FullReconciliationHours, 1, 168));
        var run = new XeroReadOnlySyncRun
        {
            OrganisationId = organisationId,
            XeroConnectionId = connection.Id,
            Trigger = trigger,
            IsFullReconciliation = full,
            StartedAt = now
        };
        db.XeroReadOnlySyncRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var accessToken = await connections.GetAccessTokenAsync(connection, actor, cancellationToken);
            var modifiedSince = full ? null : connection.LastSyncedAt?.AddMinutes(-2);
            var failures = new List<XeroSyncFailure>();
            var recordCount = 0;
            var changedCount = 0;
            var archivedCount = 0;
            var datasetCount = 0;
            TimeSpan? retryAfter = null;

            foreach (var definition in Datasets)
            {
                try
                {
                    await RenewLeaseAsync(connection, cancellationToken);
                    var result = await SyncDatasetAsync(
                        connection,
                        accessToken,
                        definition,
                        definition.SupportsModifiedSince ? modifiedSince : null,
                        full,
                        cancellationToken);
                    datasetCount++;
                    recordCount += result.Seen;
                    changedCount += result.Changed;
                    archivedCount += result.Archived;
                }
                catch (Exception ex) when (ex is XeroApiException or JsonException or InvalidOperationException)
                {
                    failures.Add(new(definition.Name, FriendlyFailure(ex)));
                    logger.LogWarning(ex, "Xero read-only dataset {Dataset} failed for organisation {OrganisationId}.",
                        definition.Key, organisationId);
                    if (ex is XeroApiException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } rateLimit)
                    {
                        retryAfter = NormaliseRetryAfter(rateLimit.RetryAfter);
                        break;
                    }
                }
            }

            var completedAt = DateTimeOffset.UtcNow;
            if (failures.Count == 0)
            {
                connection.LastSyncedAt = completedAt;
                if (full) connection.LastFullReconciliationAt = completedAt;
            }
            connection.ConsecutiveSyncFailures = failures.Count == 0 ? 0 : connection.ConsecutiveSyncFailures + 1;
            connection.NextSyncAt = completedAt.Add(
                retryAfter ?? TimeSpan.FromMinutes(
                    failures.Count == 0 ? Math.Clamp(options.SyncIntervalMinutes, 5, 1440) : 5));
            connection.LastSyncError = failures.Count == 0
                ? null
                : Limit(string.Join("; ", failures.Select(x => $"{x.Dataset}: {x.Reason}")), 2000);
            connection.SyncLeaseUntil = null;
            connection.UpdatedAt = completedAt;
            connection.UpdatedByUserId = actor;
            run.Status = failures.Count == 0 ? XeroReadOnlySyncRunStatus.Completed : XeroReadOnlySyncRunStatus.Partial;
            run.DatasetCount = datasetCount;
            run.RecordCount = recordCount;
            run.ChangedCount = changedCount;
            run.ArchivedCount = archivedCount;
            run.ErrorSummary = connection.LastSyncError;
            run.CompletedAt = completedAt;
            db.AuditEvents.Add(Audit(organisationId, actor, "XeroReadOnlySyncCompleted", run.Id, new
            {
                Trigger = trigger,
                FullReconciliation = full,
                DatasetCount = datasetCount,
                RecordCount = recordCount,
                ChangedCount = changedCount,
                ArchivedCount = archivedCount,
                FailedDatasets = failures.Select(x => x.Dataset).ToArray()
            }, nameof(XeroReadOnlySyncRun)));
            await db.SaveChangesAsync(cancellationToken);
            return new(full, datasetCount, recordCount, changedCount, archivedCount, failures);
        }
        catch (Exception ex)
        {
            var failedAt = DateTimeOffset.UtcNow;
            connection.SyncLeaseUntil = null;
            connection.ConsecutiveSyncFailures++;
            connection.NextSyncAt = failedAt.AddMinutes(5);
            connection.LastSyncError = Limit(ex.Message, 2000);
            connection.UpdatedAt = failedAt;
            connection.UpdatedByUserId = actor;
            run.Status = XeroReadOnlySyncRunStatus.Failed;
            run.ErrorSummary = connection.LastSyncError;
            run.CompletedAt = failedAt;
            try
            {
                // A host shutdown cancels the worker token. Persist the final run
                // state anyway so the next process does not inherit a phantom
                // "Running" import and an expired lease.
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception finalisationException)
            {
                logger.LogError(finalisationException,
                    "Could not finalise failed Xero read-only sync run {RunId} for organisation {OrganisationId}.",
                    run.Id, organisationId);
            }
            throw;
        }
    }

    private async Task RenewLeaseAsync(XeroConnection connection, CancellationToken cancellationToken)
    {
        var leaseUntil = DateTimeOffset.UtcNow.Add(SyncLeaseDuration);
        await db.XeroConnections
            .Where(x => x.Id == connection.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.SyncLeaseUntil, leaseUntil), cancellationToken);
        connection.SyncLeaseUntil = leaseUntil;
    }

    private async Task<DatasetSyncResult> SyncDatasetAsync(
        XeroConnection connection,
        string accessToken,
        DatasetDefinition definition,
        DateTimeOffset? modifiedSince,
        bool fullReconciliation,
        CancellationToken cancellationToken)
    {
        var existing = await db.XeroMirrorRecords
            .Where(x => x.XeroConnectionId == connection.Id && x.DatasetKey == definition.Key)
            .ToDictionaryAsync(x => x.ExternalId, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var links = await db.XeroMirrorEntityLinks
            .Where(x => x.XeroConnectionId == connection.Id && x.DatasetKey == definition.Key)
            .ToDictionaryAsync(x => x.ExternalId, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;
        var changed = 0;
        var count = 0;

        for (var page = 1; page <= MaximumPages; page++)
        {
            await RenewLeaseAsync(connection, cancellationToken);
            var endpoint = definition.Paged ? AddPage(definition.Endpoint, page) : definition.Endpoint;
            var bytes = await api.GetJsonAsync(accessToken, connection.XeroTenantId!.Value, endpoint, modifiedSince, cancellationToken);
            using var document = JsonDocument.Parse(bytes);
            var records = Records(document.RootElement, definition.RootProperty).ToList();
            foreach (var source in records)
            {
                count++;
                var externalId = ExternalId(definition.Key, source, count);
                seen.Add(externalId);
                var sourceJson = source.GetRawText();
                var hash = Hash(sourceJson);
                var archived = IsArchived(source);
                if (!existing.TryGetValue(externalId, out var mirror))
                {
                    mirror = new XeroMirrorRecord
                    {
                        OrganisationId = connection.OrganisationId,
                        XeroConnectionId = connection.Id,
                        DatasetKey = definition.Key,
                        ExternalId = externalId,
                        DisplayName = DisplayName(source, externalId),
                        SourceHash = hash,
                        SourceJson = sourceJson,
                        SourceUpdatedAt = SourceUpdatedAt(source),
                        FirstSeenAt = now,
                        LastSeenAt = now,
                        IsArchived = archived
                    };
                    db.XeroMirrorRecords.Add(mirror);
                    existing[externalId] = mirror;
                    changed++;
                }
                else
                {
                    mirror.LastSeenAt = now;
                    if (!mirror.SourceHash.Equals(hash, StringComparison.Ordinal) || mirror.IsArchived != archived)
                    {
                        mirror.DisplayName = DisplayName(source, externalId);
                        mirror.SourceHash = hash;
                        mirror.SourceJson = sourceJson;
                        mirror.SourceUpdatedAt = SourceUpdatedAt(source);
                        mirror.IsArchived = archived;
                        changed++;
                    }
                }

                await ProjectSafeMasterAsync(connection, definition.Key, externalId, source, links, cancellationToken);
            }

            if (!definition.Paged || records.Count < ExpectedPageSize) break;
            if (page == MaximumPages) throw new InvalidOperationException($"{definition.Name} exceeded the safe page limit.");
        }

        var archivedCount = 0;
        if (fullReconciliation)
        {
            foreach (var stale in existing.Values.Where(x => !seen.Contains(x.ExternalId) && !x.IsArchived))
            {
                stale.IsArchived = true;
                stale.LastSeenAt = now;
                archivedCount++;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return new(count, changed, archivedCount);
    }

    private async Task ProjectSafeMasterAsync(
        XeroConnection connection,
        string dataset,
        string externalId,
        JsonElement source,
        IDictionary<string, XeroMirrorEntityLink> links,
        CancellationToken cancellationToken)
    {
        Guid? targetId = dataset switch
        {
            "accounts" => await ProjectAccountAsync(connection.OrganisationId, externalId, source, links, cancellationToken),
            "contacts" => await ProjectContactAsync(connection.OrganisationId, externalId, source, links, cancellationToken),
            "items" => await ProjectItemAsync(connection.OrganisationId, externalId, source, links, cancellationToken),
            _ => null
        };
        if (targetId is null || links.ContainsKey(externalId)) return;
        var link = new XeroMirrorEntityLink
        {
            OrganisationId = connection.OrganisationId,
            XeroConnectionId = connection.Id,
            DatasetKey = dataset,
            ExternalId = externalId,
            TargetEntityType = dataset switch
            {
                "accounts" => nameof(LedgerAccount),
                "contacts" => nameof(BusinessParty),
                "items" => nameof(ProductItem),
                _ => "XeroMirrorRecord"
            },
            TargetEntityId = targetId.Value
        };
        db.XeroMirrorEntityLinks.Add(link);
        links[externalId] = link;
    }

    private async Task<Guid?> ProjectAccountAsync(Guid organisationId, string externalId, JsonElement source,
        IDictionary<string, XeroMirrorEntityLink> links, CancellationToken cancellationToken)
    {
        var name = Text(source, "Name").Trim();
        if (name.Length == 0) return null;
        LedgerAccount? account = null;
        if (links.TryGetValue(externalId, out var link))
            account = await db.LedgerAccounts.SingleOrDefaultAsync(x => x.Id == link.TargetEntityId && x.OrganisationId == organisationId, cancellationToken);
        var sourceCode = Text(source, "Code").Trim();
        if (account is null && sourceCode.Length > 0)
            account = await db.LedgerAccounts.SingleOrDefaultAsync(x => x.OrganisationId == organisationId && x.Code == sourceCode, cancellationToken);
        var xeroType = Text(source, "Type").ToUpperInvariant();
        if (account is null)
        {
            var code = sourceCode.Length > 0 ? sourceCode : "XERO-" + Hash(externalId)[..10];
            account = new LedgerAccount { OrganisationId = organisationId, Code = Limit(code, 20), Name = Limit(name, 160) };
            db.LedgerAccounts.Add(account);
        }
        account.Name = Limit(name, 160);
        account.Type = AccountTypeFor(xeroType);
        account.IsBankAccount = xeroType is "BANK" or "CREDITCARD";
        account.BankAccountKind = xeroType == "CREDITCARD" ? BankAccountKind.CreditCard : BankAccountKind.Bank;
        account.BankAccountNumber = NullIfBlank(Limit(Text(source, "BankAccountNumber"), 80));
        account.IsActive = !IsArchived(source);
        return account.Id;
    }

    private async Task<Guid?> ProjectContactAsync(Guid organisationId, string externalId, JsonElement source,
        IDictionary<string, XeroMirrorEntityLink> links, CancellationToken cancellationToken)
    {
        var name = Text(source, "Name").Trim();
        if (name.Length == 0) return null;
        BusinessParty? party = null;
        if (links.TryGetValue(externalId, out var link))
            party = await db.BusinessParties.SingleOrDefaultAsync(x => x.Id == link.TargetEntityId && x.OrganisationId == organisationId, cancellationToken);
        var email = NullIfBlank(Text(source, "EmailAddress").Trim());
        if (party is null)
            party = await db.BusinessParties.FirstOrDefaultAsync(x => x.OrganisationId == organisationId &&
                (x.Name == name || (email != null && x.Email == email)), cancellationToken);
        var sourceType = (Bool(source, "IsCustomer") ? PartyType.Customer : 0) |
                         (Bool(source, "IsSupplier") ? PartyType.Supplier : 0);
        if (party is null)
        {
            party = new BusinessParty
            {
                OrganisationId = organisationId,
                Name = Limit(name, 160),
                Type = sourceType == 0 ? PartyType.Customer | PartyType.Supplier : sourceType
            };
            db.BusinessParties.Add(party);
        }
        party.Name = Limit(name, 160);
        party.Email = LimitOrNull(email, 320);
        party.AccountsEmail = LimitOrNull(email, 320);
        party.Phone = LimitOrNull(FirstPhone(source), 40);
        party.Address = LimitOrNull(FirstAddress(source), 500);
        party.Tin = LimitOrNull(NullIfBlank(Text(source, "TaxNumber")), 32);
        if (sourceType != 0) party.Type = sourceType;
        party.DefaultSalesCurrency = Currency(Text(source, "DefaultCurrency"));
        party.DefaultPurchaseCurrency = Currency(Text(source, "DefaultCurrency"));
        party.IsActive = !IsArchived(source);
        return party.Id;
    }

    private async Task<Guid?> ProjectItemAsync(Guid organisationId, string externalId, JsonElement source,
        IDictionary<string, XeroMirrorEntityLink> links, CancellationToken cancellationToken)
    {
        var code = Text(source, "Code").Trim();
        if (code.Length == 0) return null;
        ProductItem? item = null;
        if (links.TryGetValue(externalId, out var link))
            item = await db.ProductItems.SingleOrDefaultAsync(x => x.Id == link.TargetEntityId && x.OrganisationId == organisationId, cancellationToken);
        if (item is null)
            item = await db.ProductItems.SingleOrDefaultAsync(x => x.OrganisationId == organisationId && x.Code == code, cancellationToken);
        if (item is null)
        {
            item = new ProductItem { OrganisationId = organisationId, Code = Limit(code, 40), Name = Limit(NullIfBlank(Text(source, "Name")) ?? code, 160) };
            db.ProductItems.Add(item);
        }
        item.Name = Limit(NullIfBlank(Text(source, "Name")) ?? code, 160);
        item.Description = LimitOrNull(NullIfBlank(Text(source, "Description")), 500);
        item.Kind = Bool(source, "IsTrackedAsInventory") ? ProductKind.TrackedItem : ProductKind.NonTrackedItem;
        item.SalePrice = NestedDecimal(source, "SalesDetails", "UnitPrice");
        item.PurchasePrice = NestedDecimal(source, "PurchaseDetails", "UnitPrice");
        item.IsActive = !IsArchived(source);
        return item.Id;
    }

    private static IEnumerable<JsonElement> Records(JsonElement root, string property)
    {
        if (TryProperty(root, property, out var records) && records.ValueKind == JsonValueKind.Array)
            foreach (var record in records.EnumerateArray()) yield return record;
    }

    private static string AddPage(string endpoint, int page) =>
        endpoint + (endpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?") + $"page={page}";

    private static string ExternalId(string dataset, JsonElement source, int ordinal)
    {
        var names = dataset switch
        {
            "accounts" => new[] { "AccountID", "Code" },
            "tax-rates" => new[] { "TaxType", "Name" },
            "currencies" => new[] { "Code" },
            "tracking" => new[] { "TrackingCategoryID", "Name" },
            "contacts" => new[] { "ContactID", "Name" },
            "items" => new[] { "ItemID", "Code" },
            "sales-invoices" or "bills" => new[] { "InvoiceID", "InvoiceNumber" },
            "sales-credits" or "supplier-credits" => new[] { "CreditNoteID", "CreditNoteNumber" },
            "payments" => new[] { "PaymentID" },
            "prepayments" => new[] { "PrepaymentID" },
            "overpayments" => new[] { "OverpaymentID" },
            "bank-transactions" => new[] { "BankTransactionID" },
            "bank-transfers" => new[] { "BankTransferID" },
            "manual-journals" => new[] { "ManualJournalID", "Narration" },
            _ => new[] { "ID", "Name" }
        };
        var value = Text(source, names);
        return string.IsNullOrWhiteSpace(value) ? $"{dataset}:{ordinal}:{Hash(source.GetRawText())[..12]}" : Limit(value, 160);
    }

    private static string DisplayName(JsonElement source, string fallback)
    {
        var value = Text(source, "InvoiceNumber", "CreditNoteNumber", "Code", "Name", "Narration", "Reference");
        return Limit(string.IsNullOrWhiteSpace(value) ? fallback : value, 240);
    }

    private static bool IsArchived(JsonElement source)
    {
        var status = Text(source, "Status", "ContactStatus");
        return status.Equals("ARCHIVED", StringComparison.OrdinalIgnoreCase) ||
               status.Equals("DELETED", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset? SourceUpdatedAt(JsonElement source)
    {
        var value = Text(source, "UpdatedDateUTC", "UpdatedDateUtc");
        if (DateTimeOffset.TryParse(value, out var timestamp)) return timestamp;
        if (value.StartsWith("/Date(", StringComparison.Ordinal) &&
            long.TryParse(value[6..].Split('+', '-', ')')[0], out var milliseconds))
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        return null;
    }

    private static string FriendlyFailure(Exception exception) => exception switch
    {
        XeroApiException x when x.StatusCode == System.Net.HttpStatusCode.TooManyRequests =>
            XeroConnectionService.RateLimitMessage(x),
        XeroApiException x when x.StatusCode == System.Net.HttpStatusCode.Unauthorized =>
            "Xero permission is missing. Reconnect and approve the requested read-only access.",
        XeroApiException x when x.StatusCode == System.Net.HttpStatusCode.Forbidden =>
            "The connected Xero user or plan does not permit this read-only dataset.",
        XeroApiException x when x.StatusCode == System.Net.HttpStatusCode.NotFound =>
            "This dataset is not available for the connected organisation.",
        _ => exception.Message
    };

    private static TimeSpan NormaliseRetryAfter(TimeSpan? retryAfter)
    {
        var seconds = retryAfter?.TotalSeconds ?? TimeSpan.FromMinutes(5).TotalSeconds;
        return TimeSpan.FromSeconds(Math.Clamp(seconds, 5, TimeSpan.FromDays(1).TotalSeconds));
    }

    private static AccountType AccountTypeFor(string xeroType) => xeroType switch
    {
        "EQUITY" => AccountType.Equity,
        "REVENUE" or "SALES" or "OTHERINCOME" => AccountType.Revenue,
        "EXPENSE" or "DIRECTCOSTS" or "OVERHEADS" or "DEPRECIATN" => AccountType.Expense,
        "LIABILITY" or "CURRLIAB" or "TERMLIAB" or "PAYGLIABILITY" => AccountType.Liability,
        _ => AccountType.Asset
    };

    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
        value = default;
        return false;
    }

    private static string Text(JsonElement element, params string[] names)
    {
        foreach (var name in names)
            if (TryProperty(element, name, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
        return string.Empty;
    }

    private static bool Bool(JsonElement element, string name) =>
        TryProperty(element, name, out var value) && (value.ValueKind == JsonValueKind.True ||
        value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) && parsed);

    private static decimal NestedDecimal(JsonElement element, string parent, string child) =>
        TryProperty(element, parent, out var nested) && TryProperty(nested, child, out var value) && value.TryGetDecimal(out var number)
            ? number
            : 0m;

    private static string? FirstPhone(JsonElement source)
    {
        if (!TryProperty(source, "Phones", out var phones) || phones.ValueKind != JsonValueKind.Array) return null;
        return phones.EnumerateArray().Select(x => Text(x, "PhoneNumber")).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
    }

    private static string? FirstAddress(JsonElement source)
    {
        if (!TryProperty(source, "Addresses", out var addresses) || addresses.ValueKind != JsonValueKind.Array) return null;
        foreach (var address in addresses.EnumerateArray())
        {
            var text = string.Join(", ", new[] { "AddressLine1", "AddressLine2", "AddressLine3", "AddressLine4", "City", "Region", "PostalCode", "Country" }
                .Select(x => Text(address, x)).Where(x => !string.IsNullOrWhiteSpace(x)));
            if (text.Length > 0) return text;
        }
        return null;
    }

    private static string? Currency(string value)
    {
        value = value.Trim().ToUpperInvariant();
        return value.Length == 3 ? value : null;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];
    private static string? LimitOrNull(string? value, int length) => string.IsNullOrWhiteSpace(value) ? null : Limit(value, length);
    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static AuditEvent Audit(Guid organisationId, string userId, string eventType, Guid entityId, object evidence,
        string entityType = nameof(XeroConnection)) => new()
    {
        OrganisationId = organisationId,
        UserId = userId,
        EventType = eventType,
        EntityType = entityType,
        EntityId = entityId.ToString(),
        JsonData = JsonSerializer.Serialize(evidence, JsonOptions)
    };

    private sealed record DatasetDefinition(
        string Key,
        string Name,
        string Endpoint,
        string RootProperty,
        bool Paged,
        bool SupportsModifiedSince);

    private sealed record DatasetSyncResult(int Seen, int Changed, int Archived);
}

public sealed class XeroReadOnlySyncWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<XeroReadOnlySyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The Xero read-only sync worker failed.");
            }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> organisations;
        using (var scope = scopeFactory.CreateScope())
        {
            organisations = await scope.ServiceProvider.GetRequiredService<XeroReadOnlySyncService>()
                .GetDueOrganisationIdsAsync(cancellationToken);
        }
        foreach (var organisationId in organisations)
        {
            using var scope = scopeFactory.CreateScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<XeroReadOnlySyncService>()
                    .RunDueAsync(organisationId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Xero read-only sync failed for organisation {OrganisationId}.", organisationId);
            }
        }
    }
}
