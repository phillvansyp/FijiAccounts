using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record SaveXeroMigrationRequest(
    string SourceOrganisationName,
    DateOnly? CutoverDate,
    bool IncludeFullHistory,
    bool XeroEntryStopped,
    bool TrialBalanceReconciled,
    bool ReceivablesReconciled,
    bool PayablesReconciled,
    bool BankReconciled,
    bool FixedAssetsReconciled,
    bool GstReconciled,
    bool ManagementApproved,
    string? Notes);

public sealed record XeroMigrationFileDownload(
    byte[] Content,
    string ContentType,
    string FileName);

public sealed record XeroMigrationRestartResult(int RetainedMasterRecords);

public sealed class XeroMigrationService(
    ApplicationDbContext db,
    TenantAccessService access,
    IImmutableDocumentStore storage)
{
    public const int MaximumBytes = 25 * 1024 * 1024;
    public static readonly string[] Categories =
    [
        "General ledger", "Trial balance", "Balance Sheet and Profit & Loss",
        "Chart of accounts", "Contacts", "Sales invoices, credits and allocations",
        "Bills, supplier credits and allocations", "Receivable Invoice Detail",
        "Payable Invoice Detail", "Bank statements and reconciliation",
        "Fixed assets and depreciation reconciliation", "GST returns and reconciliation",
        "Payroll history reports", "Inventory items", "Budgets", "Tracking categories",
        "Quotes and purchase orders", "Repeating transactions", "Document attachments",
        "Other evidence"
    ];

    public static readonly string[] RequiredCategories =
    [
        "General ledger", "Trial balance", "Balance Sheet and Profit & Loss",
        "Chart of accounts", "Contacts", "Sales invoices, credits and allocations",
        "Bills, supplier credits and allocations", "Receivable Invoice Detail",
        "Payable Invoice Detail", "Bank statements and reconciliation",
        "Fixed assets and depreciation reconciliation", "GST returns and reconciliation",
        "Payroll history reports", "Document attachments"
    ];

    private static readonly IReadOnlyDictionary<string, string[]> ApiEvidenceDatasets =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Trial balance"] = ["trial-balance"],
            ["Balance Sheet and Profit & Loss"] = ["balance-sheet", "profit-loss"],
            ["Chart of accounts"] = ["accounts"],
            ["Contacts"] = ["contacts"],
            ["Sales invoices, credits and allocations"] =
                ["sales-invoices", "sales-credits", "payments", "prepayments", "overpayments"],
            ["Bills, supplier credits and allocations"] =
                ["bills", "supplier-credits", "payments", "prepayments", "overpayments"]
        };

    public static bool HasRequiredEvidenceCategory(
        XeroMigration migration,
        string category,
        string? pendingFileCategory = null) =>
        HasRequiredEvidenceCategory(migration.Files, migration.ApiSnapshots, category, pendingFileCategory);

    private static bool HasRequiredEvidenceCategory(
        IEnumerable<XeroMigrationFile> files,
        IEnumerable<XeroApiSnapshot> apiSnapshots,
        string category,
        string? pendingFileCategory = null)
    {
        if (files.Any(file => file.Category.Equals(category, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(category, pendingFileCategory, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!ApiEvidenceDatasets.TryGetValue(category, out var requiredDatasets)) return false;
        var suppliedDatasets = apiSnapshots.Select(x => x.DatasetKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return requiredDatasets.All(suppliedDatasets.Contains);
    }

    public async Task<XeroMigration?> GetAsync(
        string userId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        if (await access.FindAsync(userId, organisationId) is null)
        {
            throw new UnauthorizedAccessException();
        }

        var migration = await db.Set<XeroMigration>()
            .AsNoTracking()
            .Include(x => x.Files)
            .Include(x => x.ApiSnapshots)
            .SingleOrDefaultAsync(x => x.OrganisationId == organisationId, cancellationToken);
        if (migration is null) return null;

        var startedAt = await XeroMigrationAttempt.StartedAtAsync(
            db, organisationId, migration.Id, cancellationToken);
        migration.Files = migration.Files
            .Where(x => XeroMigrationAttempt.IsCurrent(x.UploadedAt, startedAt)).ToList();
        migration.ApiSnapshots = migration.ApiSnapshots
            .Where(x => XeroMigrationAttempt.IsCurrent(x.RetrievedAt, startedAt)).ToList();
        return migration;
    }

    public async Task<XeroMigration> SaveAsync(
        string userId,
        Guid organisationId,
        SaveXeroMigrationRequest request,
        CancellationToken cancellationToken = default)
    {
        await DemandPrepareAsync(userId, organisationId);
        if (request.ManagementApproved && !await access.CanManageTeamAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException("Management approval requires an owner or administrator.");
        }
        var organisation = await db.Organisations.SingleAsync(
            x => x.Id == organisationId,
            cancellationToken);
        if (!organisation.CountryCode.Equals("NZ", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Xero migration is currently available for New Zealand organisations.");
        }

        var sourceName = request.SourceOrganisationName.Trim();
        if (sourceName.Length is < 2 or > 160)
        {
            throw new InvalidOperationException("Enter the Xero organisation name.");
        }

        var migration = await db.Set<XeroMigration>()
            .SingleOrDefaultAsync(x => x.OrganisationId == organisationId, cancellationToken);
        var created = migration is null;
        if (migration?.Status == XeroMigrationStatus.Complete)
            throw new InvalidOperationException("This Xero migration is complete and its cutover record is locked.");
        migration ??= new XeroMigration
        {
            OrganisationId = organisationId,
            CreatedByUserId = userId,
            UpdatedByUserId = userId
        };
        if (created) db.Set<XeroMigration>().Add(migration);

        migration.SourceOrganisationName = sourceName;
        migration.CutoverDate = request.CutoverDate;
        migration.IncludeFullHistory = request.IncludeFullHistory;
        migration.XeroEntryStopped = request.XeroEntryStopped;
        migration.TrialBalanceReconciled = request.TrialBalanceReconciled;
        migration.ReceivablesReconciled = request.ReceivablesReconciled;
        migration.PayablesReconciled = request.PayablesReconciled;
        migration.BankReconciled = request.BankReconciled;
        migration.FixedAssetsReconciled = request.FixedAssetsReconciled;
        migration.GstReconciled = request.GstReconciled;
        migration.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        var attemptStartedAt = created ? null : await XeroMigrationAttempt.StartedAtAsync(
            db, organisationId, migration.Id, cancellationToken);
        var currentFiles = created ? new List<XeroMigrationFile>() : (await db.XeroMigrationFiles.AsNoTracking()
            .Where(x => x.XeroMigrationId == migration.Id)
            .ToListAsync(cancellationToken))
            .Where(x => XeroMigrationAttempt.IsCurrent(x.UploadedAt, attemptStartedAt)).ToList();
        var currentSnapshots = created ? new List<XeroApiSnapshot>() : (await db.XeroApiSnapshots.AsNoTracking()
            .Where(x => x.XeroMigrationId == migration.Id)
            .ToListAsync(cancellationToken))
            .Where(x => XeroMigrationAttempt.IsCurrent(x.RetrievedAt, attemptStartedAt)).ToList();
        var preparedRecordsExist = !created && await db.XeroMigrationRecords
            .AnyAsync(x => x.XeroMigrationId == migration.Id, cancellationToken);
        var hasBlockingImportIssues = preparedRecordsExist && await db.XeroMigrationIssues
            .AnyAsync(x => x.XeroMigrationId == migration.Id && !x.IsResolved &&
                           x.Severity == XeroMigrationIssueSeverity.Blocking &&
                           x.Code != "LIVE_IMPORT_PENDING", cancellationToken);
        var requiredExportsSupplied = RequiredCategories.All(category =>
            HasRequiredEvidenceCategory(currentFiles, currentSnapshots, category));
        migration.ManagementApproved = request.ManagementApproved && AllChecksComplete(migration) &&
                                       requiredExportsSupplied && !hasBlockingImportIssues;
        migration.UpdatedAt = DateTimeOffset.UtcNow;
        migration.UpdatedByUserId = userId;
        migration.Status = CalculateStatus(migration, files: currentFiles, apiSnapshots: currentSnapshots);
        if (migration.CutoverDate is not null)
        {
            organisation.ConversionDate = migration.CutoverDate;
        }

        db.AuditEvents.Add(Audit(
            userId,
            organisationId,
            created ? "XeroMigrationCreated" : "XeroMigrationUpdated",
            migration.Id,
            new
            {
                migration.SourceOrganisationName,
                migration.CutoverDate,
                migration.IncludeFullHistory,
                migration.Status,
                migration.XeroEntryStopped,
                migration.TrialBalanceReconciled,
                migration.ReceivablesReconciled,
                migration.PayablesReconciled,
                migration.BankReconciled,
                migration.FixedAssetsReconciled,
                migration.GstReconciled,
                migration.ManagementApproved,
                SourceFileCount = currentFiles.Count
            }));
        await db.SaveChangesAsync(cancellationToken);
        return migration;
    }

    public async Task<XeroMigrationFile> AddFileAsync(
        string userId,
        Guid organisationId,
        string category,
        string filename,
        string? contentType,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        await DemandPrepareAsync(userId, organisationId);
        if (!Categories.Contains(category))
        {
            throw new InvalidOperationException("Choose a valid Xero export type.");
        }
        if (string.IsNullOrWhiteSpace(filename) || filename.Length > 255 || Path.GetFileName(filename) != filename)
        {
            throw new InvalidOperationException("The source filename is invalid.");
        }
        var extension = Path.GetExtension(filename).ToLowerInvariant();
        if (extension is not (".csv" or ".xlsx" or ".pdf" or ".zip") || content.Length == 0 || content.Length > MaximumBytes)
        {
            throw new InvalidOperationException("Choose a CSV, XLSX, PDF or ZIP file no larger than 25 MB.");
        }
        if (!HasExpectedSignature(extension, content))
        {
            throw new InvalidOperationException("The file content does not match its filename.");
        }

        var migration = await db.Set<XeroMigration>()
            .SingleOrDefaultAsync(x => x.OrganisationId == organisationId, cancellationToken)
            ?? throw new InvalidOperationException("Save the migration setup before adding exports.");
        var attemptStartedAt = await XeroMigrationAttempt.StartedAtAsync(
            db, organisationId, migration.Id, cancellationToken);
        var currentFiles = (await db.XeroMigrationFiles.AsNoTracking()
            .Where(x => x.XeroMigrationId == migration.Id)
            .ToListAsync(cancellationToken))
            .Where(x => XeroMigrationAttempt.IsCurrent(x.UploadedAt, attemptStartedAt)).ToList();
        var currentSnapshots = (await db.XeroApiSnapshots.AsNoTracking()
            .Where(x => x.XeroMigrationId == migration.Id)
            .ToListAsync(cancellationToken))
            .Where(x => XeroMigrationAttempt.IsCurrent(x.RetrievedAt, attemptStartedAt)).ToList();
        var stored = storage.Stage(organisationId, userId, content);
        var file = new XeroMigrationFile
        {
            OrganisationId = organisationId,
            XeroMigrationId = migration.Id,
            Category = category,
            FileName = filename,
            ContentType = NormaliseContentType(extension, contentType),
            Size = content.LongLength,
            Sha256 = stored.Sha256,
            ImmutableDocumentObjectId = stored.Id,
            UploadedByUserId = userId
        };
        db.Set<XeroMigrationFile>().Add(file);
        migration.UpdatedAt = DateTimeOffset.UtcNow;
        migration.UpdatedByUserId = userId;
        migration.Status = CalculateStatus(
            migration, category, currentFiles, currentSnapshots);
        db.AuditEvents.Add(Audit(userId, organisationId, "XeroMigrationFileAdded", file.Id, new
        {
            migration.Id,
            file.Category,
            file.FileName,
            file.Size,
            file.Sha256
        }));
        await db.SaveChangesAsync(cancellationToken);
        return file;
    }

    public async Task<XeroMigrationFileDownload?> ReadFileAsync(
        string userId,
        Guid organisationId,
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        if (await access.FindAsync(userId, organisationId) is null) return null;
        var file = await db.Set<XeroMigrationFile>().AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == fileId && x.OrganisationId == organisationId,
            cancellationToken);
        if (file is null) return null;
        var content = await storage.ReadVerifiedAsync(organisationId, file.ImmutableDocumentObjectId, cancellationToken);
        db.AuditEvents.Add(Audit(userId, organisationId, "XeroMigrationFileOpened", file.Id, new
        {
            file.Category,
            file.FileName,
            file.Sha256
        }));
        await db.SaveChangesAsync(cancellationToken);
        return new(content, file.ContentType, file.FileName);
    }

    public async Task<XeroMigration> CompleteAsync(
        string userId, Guid organisationId, CancellationToken cancellationToken = default)
    {
        if (!await access.CanManageTeamAsync(userId, organisationId))
            throw new UnauthorizedAccessException("Only an owner or administrator can complete the migration.");
        var migration = await db.XeroMigrations.SingleOrDefaultAsync(
            x => x.OrganisationId == organisationId, cancellationToken)
            ?? throw new InvalidOperationException("The Xero migration was not found.");
        if (!migration.ManagementApproved || !AllChecksComplete(migration))
            throw new InvalidOperationException("Complete all seven reconciliations and record management approval first.");
        if (await db.XeroMigrationIssues.AnyAsync(x => x.XeroMigrationId == migration.Id &&
                !x.IsResolved && x.Severity == XeroMigrationIssueSeverity.Blocking, cancellationToken))
            throw new InvalidOperationException("Resolve every blocking migration issue before completion.");
        if (!await db.AuditEvents.AnyAsync(x => x.OrganisationId == organisationId &&
                x.EventType == "XeroOpeningBalancePosted" && x.EntityId == migration.Id.ToString(), cancellationToken))
            throw new InvalidOperationException("Create the verified cutover opening balance first.");

        migration.Status = XeroMigrationStatus.Complete;
        migration.UpdatedAt = DateTimeOffset.UtcNow;
        migration.UpdatedByUserId = userId;
        db.AuditEvents.Add(Audit(userId, organisationId, "XeroMigrationCompleted", migration.Id, new
        {
            migration.CutoverDate,
            migration.SourceOrganisationName,
            CompletedAt = migration.UpdatedAt
        }));
        await db.SaveChangesAsync(cancellationToken);
        return migration;
    }

    public async Task<XeroMigrationRestartResult> RestartAsync(
        string userId, Guid organisationId, CancellationToken cancellationToken = default)
    {
        if (!await access.CanManageTeamAsync(userId, organisationId))
            throw new UnauthorizedAccessException("Only an owner or administrator can restart the migration.");

        var migration = await db.XeroMigrations.SingleOrDefaultAsync(
            x => x.OrganisationId == organisationId, cancellationToken)
            ?? throw new InvalidOperationException("The Xero migration was not found.");
        var cutoverPosted = await db.AuditEvents.AnyAsync(x =>
            x.OrganisationId == organisationId &&
            x.EventType == "XeroOpeningBalancePosted" &&
            x.EntityId == migration.Id.ToString(), cancellationToken);
        if (migration.Status == XeroMigrationStatus.Complete || cutoverPosted)
            throw new InvalidOperationException(
                "This migration has posted its live cutover and cannot be restarted. Reverse the posted accounting records through the controlled accounting workflow.");

        var previousSource = migration.SourceOrganisationName;
        var previousCutover = migration.CutoverDate;
        var records = await db.XeroMigrationRecords
            .Where(x => x.XeroMigrationId == migration.Id).ToListAsync(cancellationToken);
        var retainedMasterRecords = records.Count(x =>
            x.Status == XeroMigrationRecordStatus.Imported &&
            x.TargetEntityType is nameof(LedgerAccount) or nameof(BusinessParty) or
                nameof(OrganisationCurrency) or nameof(ProductItem));
        var issues = await db.XeroMigrationIssues
            .Where(x => x.XeroMigrationId == migration.Id).ToListAsync(cancellationToken);
        var mappings = await db.XeroMigrationMappings
            .Where(x => x.XeroMigrationId == migration.Id).ToListAsync(cancellationToken);
        var runs = await db.XeroMigrationImportRuns
            .Where(x => x.XeroMigrationId == migration.Id).ToListAsync(cancellationToken);
        var snapshots = await db.XeroApiSnapshots
            .Where(x => x.XeroMigrationId == migration.Id).ToListAsync(cancellationToken);
        var files = await db.XeroMigrationFiles
            .Where(x => x.XeroMigrationId == migration.Id).ToListAsync(cancellationToken);

        db.XeroMigrationIssues.RemoveRange(issues);
        db.XeroMigrationMappings.RemoveRange(mappings);
        db.XeroMigrationRecords.RemoveRange(records);
        db.XeroMigrationImportRuns.RemoveRange(runs);
        var connection = await db.XeroConnections.SingleOrDefaultAsync(
            x => x.OrganisationId == organisationId, cancellationToken);
        if (connection is not null)
        {
            connection.XeroConnectionId = null;
            connection.XeroTenantId = null;
            connection.XeroTenantName = null;
            connection.LastSyncedAt = null;
            connection.LastSyncError = null;
            connection.UpdatedAt = DateTimeOffset.UtcNow;
            connection.UpdatedByUserId = userId;
        }

        migration.SourceOrganisationName = string.Empty;
        migration.CutoverDate = null;
        migration.IncludeFullHistory = true;
        migration.Status = XeroMigrationStatus.Preparing;
        migration.XeroEntryStopped = false;
        migration.TrialBalanceReconciled = false;
        migration.ReceivablesReconciled = false;
        migration.PayablesReconciled = false;
        migration.BankReconciled = false;
        migration.FixedAssetsReconciled = false;
        migration.GstReconciled = false;
        migration.ManagementApproved = false;
        migration.Notes = null;
        var restartedAt = DateTimeOffset.UtcNow;
        migration.UpdatedAt = restartedAt;
        migration.UpdatedByUserId = userId;

        var organisation = await db.Organisations.SingleAsync(
            x => x.Id == organisationId, cancellationToken);
        if (organisation.ConversionDate == previousCutover)
            organisation.ConversionDate = null;

        var restartAudit = Audit(userId, organisationId, "XeroMigrationRestarted", migration.Id, new
        {
            PreviousSourceOrganisationName = previousSource,
            PreviousCutoverDate = previousCutover,
            RetainedPreviousSnapshots = snapshots.Count,
            RetainedPreviousEvidenceLinks = files.Count,
            RemovedPreparedRecords = records.Count,
            RemovedMappings = mappings.Count,
            RemovedIssues = issues.Count,
            RemovedRuns = runs.Count,
            RetainedMasterRecords = retainedMasterRecords,
            ImmutableSourceDocumentsRetained = true
        });
        restartAudit.OccurredAt = restartedAt;
        db.AuditEvents.Add(restartAudit);
        await db.SaveChangesAsync(cancellationToken);
        return new(retainedMasterRecords);
    }

    private async Task DemandPrepareAsync(string userId, Guid organisationId)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException("You cannot prepare this migration.");
        }
    }

    private static XeroMigrationStatus CalculateStatus(
        XeroMigration migration,
        string? addedCategory = null,
        IEnumerable<XeroMigrationFile>? files = null,
        IEnumerable<XeroApiSnapshot>? apiSnapshots = null)
    {
        if (migration.ManagementApproved && AllChecksComplete(migration)) return XeroMigrationStatus.ApprovedForCutover;
        if (migration.TrialBalanceReconciled || migration.ReceivablesReconciled || migration.PayablesReconciled ||
            migration.BankReconciled || migration.FixedAssetsReconciled || migration.GstReconciled)
            return XeroMigrationStatus.Reconciliation;
        return RequiredCategories.All(category => HasRequiredEvidenceCategory(
                   files ?? migration.Files, apiSnapshots ?? migration.ApiSnapshots, category, addedCategory)) && migration.CutoverDate is not null
            ? XeroMigrationStatus.ReadyForImport
            : XeroMigrationStatus.Preparing;
    }

    public static bool AllChecksComplete(XeroMigration migration) =>
        migration.XeroEntryStopped && migration.TrialBalanceReconciled && migration.ReceivablesReconciled &&
        migration.PayablesReconciled && migration.BankReconciled && migration.FixedAssetsReconciled &&
        migration.GstReconciled;

    private static bool HasExpectedSignature(string extension, byte[] content) => extension switch
    {
        ".pdf" => content.Length >= 5 && content.AsSpan(0, 5).SequenceEqual("%PDF-"u8),
        ".xlsx" or ".zip" => content.Length >= 4 && content[0] == (byte)'P' && content[1] == (byte)'K',
        ".csv" => !content.AsSpan().Contains((byte)0),
        _ => false
    };

    private static string NormaliseContentType(string extension, string? supplied) => extension switch
    {
        ".pdf" => "application/pdf",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".zip" => "application/zip",
        ".csv" => "text/csv",
        _ => supplied ?? "application/octet-stream"
    };

    private static AuditEvent Audit(string userId, Guid organisationId, string eventType, Guid entityId, object evidence) => new()
    {
        OrganisationId = organisationId,
        UserId = userId,
        EventType = eventType,
        EntityType = nameof(XeroMigration),
        EntityId = entityId.ToString(),
        JsonData = JsonSerializer.Serialize(evidence)
    };
}
