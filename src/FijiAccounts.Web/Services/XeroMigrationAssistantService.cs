using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public enum XeroMigrationAssistantAction
{
    SaveSetup,
    ConnectXero,
    CopySource,
    RunPreparation,
    SupplyEvidence,
    ResolveMappings,
    CompleteReconciliation,
    RecordApproval,
    BuildCutover,
    CompleteMigration,
    Finished
}

public sealed record XeroMigrationAssistantCheck(
    string Key,
    string Title,
    string Detail,
    bool Passed,
    bool IsControl = false);

public sealed record XeroMigrationAssistantSummary(
    string Headline,
    string Guidance,
    XeroMigrationAssistantAction NextAction,
    IReadOnlyList<XeroMigrationAssistantCheck> Checks,
    bool CutoverExists,
    bool PostCutoverVerified,
    int PassedCount,
    int TotalCount);

/// <summary>
/// Turns the detailed Xero migration controls into a plain-language, ordered workflow.
/// All decisions are derived from accounting records and audit evidence. It does not
/// infer monetary values or approve/post a cutover.
/// </summary>
public sealed class XeroMigrationAssistantService(
    ApplicationDbContext db,
    TenantAccessService access,
    XeroImportPreparationService preparation)
{
    public async Task<XeroMigrationAssistantSummary> VerifyAndRecordAsync(
        string userId, Guid organisationId, CancellationToken cancellationToken = default)
    {
        var summary = await GetAsync(userId, organisationId, cancellationToken);
        if (!summary.PostCutoverVerified)
            throw new InvalidOperationException("The post-cutover checks are not complete. Review the pending checklist items before completing the migration.");

        var migrationId = await db.XeroMigrations.AsNoTracking()
            .Where(x => x.OrganisationId == organisationId)
            .Select(x => x.Id)
            .SingleAsync(cancellationToken);
        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = organisationId,
            EventType = "XeroMigrationPostCutoverVerified",
            EntityType = nameof(XeroMigration),
            EntityId = migrationId.ToString(),
            UserId = userId,
            JsonData = JsonSerializer.Serialize(new
            {
                summary.PassedCount,
                summary.TotalCount,
                summary.CutoverExists,
                summary.PostCutoverVerified,
                Checks = summary.Checks.Select(x => new { x.Key, x.Title, x.Passed })
            })
        });
        await db.SaveChangesAsync(cancellationToken);
        return summary;
    }

    public async Task<XeroMigrationAssistantSummary> GetAsync(
        string userId, Guid organisationId, CancellationToken cancellationToken = default)
    {
        if (await access.FindAsync(userId, organisationId) is null)
            throw new UnauthorizedAccessException();

        var migration = await db.XeroMigrations.AsNoTracking()
            .Include(x => x.Files)
            .Include(x => x.ApiSnapshots)
            .SingleOrDefaultAsync(x => x.OrganisationId == organisationId, cancellationToken);
        if (migration is null)
            return Summary("Start with the migration setup",
                "Confirm the Xero organisation and choose the first live day in Account Island.",
                XeroMigrationAssistantAction.SaveSetup, [], false, false);

        var workspace = await preparation.GetWorkspaceAsync(userId, organisationId, cancellationToken);
        var latestSnapshotCount = (await db.XeroApiSnapshots.AsNoTracking()
                .Where(x => x.XeroMigrationId == migration.Id)
                .Select(x => x.DatasetKey)
                .ToListAsync(cancellationToken))
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var evidenceComplete = XeroMigrationService.RequiredCategories.All(category =>
            XeroMigrationService.HasRequiredEvidenceCategory(migration, category));
        var suppliedEvidenceCount = XeroMigrationService.RequiredCategories.Count(category =>
            XeroMigrationService.HasRequiredEvidenceCategory(migration, category));
        var mappingsComplete = workspace.Mappings.All(x => x.Status != XeroMigrationMappingStatus.Suggested);
        var preparationComplete = workspace.SourceRecordCount > 0 && workspace.LastRun is not null;
        var masterDataComplete = preparationComplete && workspace.ImportedCount > 0;
        var controlsComplete = XeroMigrationService.AllChecksComplete(migration);
        var noBlockingIssues = workspace.BlockingCount == 0;

        var openingAudits = await db.AuditEvents.AsNoTracking()
            .Where(x => x.OrganisationId == organisationId &&
                        x.EventType == "XeroOpeningBalancePosted" &&
                        x.EntityId == migration.Id.ToString())
            .ToListAsync(cancellationToken);
        var openingAudit = openingAudits.OrderByDescending(x => x.OccurredAt).FirstOrDefault();
        var openingJournalId = OpeningJournalId(openingAudit?.JsonData);
        var openingJournal = openingJournalId is null
            ? null
            : await db.PostedJournals.AsNoTracking().Include(x => x.Lines)
                .SingleOrDefaultAsync(x => x.Id == openingJournalId.Value && x.OrganisationId == organisationId,
                    cancellationToken);
        var cutoverExists = openingJournal is not null;
        var openingBalanced = openingJournal is not null && openingJournal.Lines.Count > 0 &&
                              openingJournal.Lines.Sum(x => x.Debit) == openingJournal.Lines.Sum(x => x.Credit);

        var unresolvedLiveRecords = await db.XeroMigrationRecords.AsNoTracking()
            .CountAsync(x => x.XeroMigrationId == migration.Id &&
                new[] { "sales-invoices", "bills", "sales-credits", "supplier-credits", "assets" }
                    .Contains(x.DatasetKey) &&
                (x.Status == XeroMigrationRecordStatus.Ready ||
                 x.Status == XeroMigrationRecordStatus.NeedsMapping ||
                 x.Status == XeroMigrationRecordStatus.NeedsReview ||
                 x.Status == XeroMigrationRecordStatus.Failed), cancellationToken);
        var staleReviewMessages = await db.XeroMigrationRecords.AsNoTracking()
            .CountAsync(x => x.XeroMigrationId == migration.Id &&
                             (x.Status == XeroMigrationRecordStatus.Imported ||
                              x.Status == XeroMigrationRecordStatus.Archived) &&
                             x.ReviewMessage != null, cancellationToken);
        var historyArchived = cutoverExists && await db.XeroMigrationRecords.AsNoTracking()
            .AnyAsync(x => x.XeroMigrationId == migration.Id && x.Status == XeroMigrationRecordStatus.Archived,
                cancellationToken);
        var postCutoverVerified = cutoverExists && openingBalanced && unresolvedLiveRecords == 0 &&
                                  staleReviewMessages == 0 && noBlockingIssues;

        var checks = new List<XeroMigrationAssistantCheck>
        {
            new("setup", "Cutover is defined",
                migration.CutoverDate is null
                    ? "Choose the first day Account Island becomes the live accounting system."
                    : $"Account Island becomes live on {migration.CutoverDate:dd MMM yyyy}.",
                migration.CutoverDate is not null),
            new("source", "Xero source is retained",
                latestSnapshotCount == 0
                    ? "Connect Xero and copy its accounting datasets into protected staging."
                    : $"{latestSnapshotCount} current Xero datasets are held in protected staging.",
                latestSnapshotCount > 0),
            new("evidence", "Control evidence is complete",
                evidenceComplete
                    ? $"All {XeroMigrationService.RequiredCategories.Length} required evidence sets are retained with fingerprints."
                    : $"{suppliedEvidenceCount} of {XeroMigrationService.RequiredCategories.Length} required evidence sets are supplied.",
                evidenceComplete),
            new("preparation", "Records are staged and checked",
                preparationComplete
                    ? $"{workspace.SourceRecordCount:N0} source records across {workspace.DatasetCount} datasets have been checked."
                    : "Run guided preparation to index, classify and validate the copied records.",
                preparationComplete),
            new("master-data", "Master data is ready",
                masterDataComplete
                    ? $"{workspace.ImportedCount:N0} accounts, contacts, currencies or items are linked or added."
                    : "Guided preparation adds safe master data before any financial posting.",
                masterDataComplete),
            new("mappings", "Mappings are decided",
                mappingsComplete ? "Every discovered value has a confirmed destination or an explicit exclusion."
                    : $"{workspace.Mappings.Count(x => x.Status == XeroMigrationMappingStatus.Suggested)} mapping decisions remain.",
                mappingsComplete),
            new("exceptions", "No blocking exceptions remain",
                noBlockingIssues ? "Automated validation found no unresolved blocking exception."
                    : $"Resolve {workspace.BlockingCount} blocking exception(s) before approval.",
                noBlockingIssues),
            new("controls", "Seven control balances are reconciled",
                controlsComplete ? "Trial balance, receivables, payables, banks, fixed assets and GST are signed off."
                    : "Complete the seven reconciliation confirmations using the retained source reports.",
                controlsComplete, true),
            new("approval", "Management approval is recorded",
                migration.ManagementApproved ? "Approval is preserved in the audit trail."
                    : "An owner or administrator must approve the verified cutover.",
                migration.ManagementApproved, true),
            new("opening", "Opening journal is balanced",
                openingJournal is null ? "The live opening journal has not been created."
                    : openingBalanced
                        ? $"{openingJournal.Reference} is balanced at {openingJournal.Lines.Sum(x => x.Debit):N2}."
                        : "The opening journal does not balance and requires review.",
                openingBalanced, true),
            new("operational", "Open items are linked",
                cutoverExists && unresolvedLiveRecords == 0
                    ? "Open invoices, bills, supplier credits and assets have no unresolved live records."
                    : cutoverExists
                        ? $"{unresolvedLiveRecords} live record(s) still require a destination."
                        : "Open customer and supplier items will be created with the approved cutover.",
                cutoverExists && unresolvedLiveRecords == 0, true),
            new("archive", "History and review notes are clean",
                !cutoverExists ? "Historical transactions will be retained without reposting them."
                    : staleReviewMessages > 0 ? $"{staleReviewMessages} retained record(s) still carry a review message."
                    : historyArchived ? "Historical transactions are retained without duplicating the opening position."
                    : "No historical transaction records required archiving for this source.",
                cutoverExists && staleReviewMessages == 0, true)
        };

        if (migration.Status == XeroMigrationStatus.Complete && postCutoverVerified)
            return Summary("Migration complete and verified",
                "Account Island holds the balanced opening position, open operational records and protected Xero history.",
                XeroMigrationAssistantAction.Finished, checks, cutoverExists, true);
        if (migration.CutoverDate is null)
            return Summary("Finish the migration setup", "Choose the first live day before copying data.",
                XeroMigrationAssistantAction.SaveSetup, checks, cutoverExists, postCutoverVerified);
        if (latestSnapshotCount == 0)
            return Summary("Copy the source data", "Connect Xero, select the correct organisation and copy its datasets.",
                XeroMigrationAssistantAction.ConnectXero, checks, cutoverExists, postCutoverVerified);
        if (!preparationComplete || !masterDataComplete)
            return Summary("Run guided preparation",
                "The assistant can safely index the source, add master data, refresh classifications and recheck exceptions.",
                XeroMigrationAssistantAction.RunPreparation, checks, cutoverExists, postCutoverVerified);
        if (!mappingsComplete || !noBlockingIssues)
            return Summary("Resolve the remaining decisions",
                "Review the highlighted mappings and exceptions. The assistant will never guess accounting destinations.",
                XeroMigrationAssistantAction.ResolveMappings, checks, cutoverExists, postCutoverVerified);
        if (!evidenceComplete)
            return Summary("Complete the evidence pack",
                "Add the missing control reports so the cutover can be independently checked later.",
                XeroMigrationAssistantAction.SupplyEvidence, checks, cutoverExists, postCutoverVerified);
        if (!controlsComplete)
            return Summary("Reconcile the control balances",
                "Compare Account Island to the final Xero reports and confirm all seven controls.",
                XeroMigrationAssistantAction.CompleteReconciliation, checks, cutoverExists, postCutoverVerified);
        if (!migration.ManagementApproved)
            return Summary("Ready for management approval",
                "The source, evidence, mappings and controls are ready for an owner or administrator to approve.",
                XeroMigrationAssistantAction.RecordApproval, checks, cutoverExists, postCutoverVerified);
        if (!cutoverExists)
            return Summary("Ready to create the live cutover",
                "Approval is recorded. Create the balanced opening journal and open operational records.",
                XeroMigrationAssistantAction.BuildCutover, checks, false, false);
        if (migration.Status != XeroMigrationStatus.Complete)
            return Summary("Cutover verified — complete the migration",
                "The opening journal and live records passed the automated post-cutover checks.",
                XeroMigrationAssistantAction.CompleteMigration, checks, true, postCutoverVerified);

        return Summary("Migration needs a final review",
            "The migration is marked complete, but one or more post-cutover checks need attention.",
            XeroMigrationAssistantAction.ResolveMappings, checks, cutoverExists, postCutoverVerified);
    }

    private static XeroMigrationAssistantSummary Summary(
        string headline, string guidance, XeroMigrationAssistantAction nextAction,
        IReadOnlyList<XeroMigrationAssistantCheck> checks, bool cutoverExists, bool postCutoverVerified) =>
        new(headline, guidance, nextAction, checks, cutoverExists, postCutoverVerified,
            checks.Count(x => x.Passed), checks.Count);

    private static Guid? OpeningJournalId(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("OpeningJournalId", out var value)) return null;
            return Guid.TryParse(value.GetString(), out var id) ? id : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
