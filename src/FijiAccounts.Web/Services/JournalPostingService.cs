using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record JournalLineInput(
    Guid AccountId,
    string Description,
    decimal Debit,
    decimal Credit,
    Guid? BranchId = null,
    Guid? DivisionId = null,
    Guid? ProjectId = null,
    Guid? ProjectCostCodeId = null);

public sealed record JournalPostRequest(
    Guid OrganisationId,
    DateOnly Date,
    string Reference,
    string? Description,
    IReadOnlyList<JournalLineInput> Lines,
    Guid? BranchId = null,
    Guid? DivisionId = null,
    JournalPurpose Purpose = JournalPurpose.Standard,
    Guid? AdjustmentPeriodId = null,
    string? ApprovalReference = null);

public sealed class JournalPostingService(
    ApplicationDbContext db,
    TenantAccessService tenantAccess,
    BankReconciliationService reconciliation)
{
    internal Task<PostedJournal> PostAutomaticallyAsync(
        JournalPostRequest request,
        CancellationToken cancellationToken = default)
    {
        return PostCoreAsync(
            "system",
            request,
            cancellationToken,
            PostingAuthorization.System);
    }

    internal Task<PostedJournal> PostApprovedWorkflowAsync(
        string userId,
        JournalPostRequest request,
        CancellationToken cancellationToken = default) =>
        PostCoreAsync(
            userId,
            request,
            cancellationToken,
            PostingAuthorization.ApprovedWorkflow);

    public Task<PostedJournal> PostAsync(
        string userId,
        JournalPostRequest request,
        CancellationToken cancellationToken = default) =>
        PostCoreAsync(
            userId,
            request,
            cancellationToken,
            PostingAuthorization.Standard);

    private async Task<PostedJournal> PostCoreAsync(
        string userId,
        JournalPostRequest request,
        CancellationToken cancellationToken,
        PostingAuthorization authorization)
    {
        if (authorization == PostingAuthorization.Standard &&
            !await tenantAccess.CanPostJournalsAsync(
                userId,
                request.OrganisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot post journals for this organisation.");
        }
        if (await db.AccountingPeriods.AnyAsync(x => x.OrganisationId == request.OrganisationId && x.IsLocked && request.Date >= x.StartsOn && request.Date <= x.EndsOn, cancellationToken)) throw new InvalidOperationException("The accounting period is locked.");

        AccountingPeriod? adjustmentPeriod = null;
        string? approvalReference = null;
        if (request.Purpose == JournalPurpose.YearEndAdjustment)
        {
            if (request.AdjustmentPeriodId is not Guid adjustmentPeriodId)
            {
                throw new InvalidOperationException(
                    "Select the accounting period for the year-end adjustment.");
            }

            approvalReference = request.ApprovalReference?.Trim();
            if (string.IsNullOrWhiteSpace(approvalReference))
            {
                throw new InvalidOperationException(
                    "Enter the accountant approval or working-paper reference.");
            }

            if (approvalReference.Length > 80)
            {
                throw new InvalidOperationException(
                    "The approval reference cannot exceed 80 characters.");
            }

            adjustmentPeriod = await db.AccountingPeriods.SingleOrDefaultAsync(
                x => x.Id == adjustmentPeriodId &&
                     x.OrganisationId == request.OrganisationId,
                cancellationToken)
                ?? throw new InvalidOperationException("Accounting period not found.");
            if (adjustmentPeriod.IsLocked)
            {
                throw new InvalidOperationException(
                    "Reopen the accounting period before posting a year-end adjustment.");
            }

            if (request.Date < adjustmentPeriod.StartsOn ||
                request.Date > adjustmentPeriod.EndsOn)
            {
                throw new InvalidOperationException(
                    "The adjustment date must be inside the selected accounting period.");
            }
        }
        else if (request.AdjustmentPeriodId is not null ||
                 !string.IsNullOrWhiteSpace(request.ApprovalReference))
        {
            throw new InvalidOperationException(
                "Adjustment evidence can only be attached to a year-end adjustment journal.");
        }

        var accountIds = request.Lines.Select(x => x.AccountId).Distinct().ToArray();
        // SQLite persists Guid values as text. Older seeded data used lower-case values,
        // while EF parameters use upper-case values, making SQL IN comparisons reject
        // otherwise identical IDs. Keep the database query tenant-scoped, then match the
        // small chart of accounts using Guid equality in .NET.
        var organisationAccounts = await db.LedgerAccounts
            .Where(x => x.OrganisationId == request.OrganisationId && x.IsActive)
            .ToListAsync(cancellationToken);
        var accounts = organisationAccounts
            .Where(x => accountIds.Contains(x.Id))
            .ToDictionary(x => x.Id);
        if (accounts.Count != accountIds.Length) throw new InvalidOperationException("Every account must be active and belong to the selected organisation.");

        var activeBranches =
            await db.Branches
                .AsNoTracking()
                .Include(x => x.Divisions.Where(division => division.IsActive))
                .Where(x =>
                    x.OrganisationId == request.OrganisationId &&
                    x.IsActive)
                .ToListAsync(cancellationToken);
        var dimensions =
            request.Lines
                .Select(line => ResolveDimension(
                    activeBranches,
                    line.BranchId ?? request.BranchId,
                    line.DivisionId ?? request.DivisionId))
                .ToList();
        var projectIds = request.Lines
            .Where(x => x.ProjectId is not null)
            .Select(x => x.ProjectId!.Value)
            .Distinct()
            .ToArray();
        var organisationProjects = await db.Projects.AsNoTracking()
            .Include(x => x.CostCodes)
            .Where(x => x.OrganisationId == request.OrganisationId && projectIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
        var projects = organisationProjects
            .Where(x => projectIds.Contains(x.Id))
            .ToDictionary(x => x.Id);
        if (projects.Count != projectIds.Length)
        {
            throw new InvalidOperationException(
                "Every project must belong to the selected organisation.");
        }
        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = request.Lines[index];
            if (line.ProjectCostCodeId is not null && line.ProjectId is null)
            {
                throw new InvalidOperationException(
                    "A project cost code requires a project.");
            }
            if (line.ProjectId is not Guid projectId) continue;
            var project = projects[projectId];
            if (project.Status is ProjectStatus.Draft or ProjectStatus.Cancelled)
            {
                throw new InvalidOperationException(
                    "Transactions can only use active, on-hold, or completed projects.");
            }
            if (project.BranchId != dimensions[index].BranchId ||
                project.DivisionId != dimensions[index].DivisionId)
            {
                throw new InvalidOperationException(
                    "The project must belong to the journal line's branch and division.");
            }
            if (line.ProjectCostCodeId is Guid costCodeId &&
                !project.CostCodes.Any(x => x.Id == costCodeId && x.IsActive))
            {
                throw new InvalidOperationException(
                    "The project cost code must be active and belong to the selected project.");
            }
        }
        if (authorization != PostingAuthorization.System)
        {
            foreach (var dimension in dimensions.Distinct())
            {
                if (!await tenantAccess.CanAccessDimensionAsync(
                    userId,
                    request.OrganisationId,
                    dimension.BranchId,
                    dimension.DivisionId,
                    cancellationToken))
                {
                    throw new UnauthorizedAccessException(
                        "You cannot post transactions to the selected branch or division.");
                }
            }
        }

        var bankAccountIds =
    accounts.Values
        .Where(x => x.IsBankAccount)
        .Select(x => x.Id)
        .ToArray();

foreach (var bankAccountId in bankAccountIds)
{
    if (await reconciliation.IsInsideCompletedReconciliationAsync(
            request.OrganisationId,
            bankAccountId,
            request.Date,
            cancellationToken))
    {
        throw new InvalidOperationException(
            "A journal cannot post to a bank account inside a completed reconciliation period.");
    }
}

        _ = new JournalEntry(request.OrganisationId, request.Date, request.Reference,
            request.Lines.Select(x => new JournalLine(accounts[x.AccountId].Code, x.Description, x.Debit, x.Credit)));

        var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        var sequence = (await db.PostedJournals.Where(x => x.OrganisationId == request.OrganisationId).MaxAsync(x => (long?)x.SequenceNumber, cancellationToken) ?? 0) + 1;
        var journal = new PostedJournal
        {
            OrganisationId = request.OrganisationId, SequenceNumber = sequence, EntryDate = request.Date,
            Reference = request.Reference.Trim(), Description = request.Description?.Trim(), PostedAt = DateTimeOffset.UtcNow, PostedByUserId = userId,
            Purpose = request.Purpose,
            AdjustmentPeriodId = adjustmentPeriod?.Id,
            ApprovalReference = approvalReference,
            Lines = request.Lines.Select((x, index) => new PostedJournalLine { LedgerAccountId = x.AccountId, BranchId = dimensions[index].BranchId, DivisionId = dimensions[index].DivisionId, ProjectId = x.ProjectId, ProjectCostCodeId = x.ProjectCostCodeId, Description = x.Description.Trim(), Debit = x.Debit, Credit = x.Credit }).ToList()
        };
        db.PostedJournals.Add(journal);
        db.AuditEvents.Add(new AuditEvent { OrganisationId = request.OrganisationId, EventType = request.Purpose == JournalPurpose.YearEndAdjustment ? "YearEndAdjustmentPosted" : "JournalPosted", EntityType = nameof(PostedJournal), EntityId = journal.Id.ToString(), UserId = userId, JsonData = JsonSerializer.Serialize(new { journal.SequenceNumber, journal.EntryDate, journal.Reference, journal.Purpose, journal.AdjustmentPeriodId, journal.ApprovalReference }) });
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) { await transaction.CommitAsync(cancellationToken); await transaction.DisposeAsync(); }
        return journal;
    }

    private static PostingDimension ResolveDimension(
        IReadOnlyList<Branch> activeBranches,
        Guid? requestedBranchId,
        Guid? requestedDivisionId)
    {
        Branch? branch = null;
        Division? division = null;

        if (requestedDivisionId is Guid divisionId)
        {
            branch = activeBranches.SingleOrDefault(x =>
                x.Divisions.Any(candidate => candidate.Id == divisionId));
            division = branch?.Divisions.Single(x => x.Id == divisionId);

            if (division is null)
            {
                throw new InvalidOperationException(
                    "The selected division must be active and belong to this organisation.");
            }
        }

        if (requestedBranchId is Guid branchId)
        {
            var selectedBranch =
                activeBranches.SingleOrDefault(x => x.Id == branchId)
                ?? throw new InvalidOperationException(
                    "The selected branch must be active and belong to this organisation.");

            if (branch is not null && branch.Id != selectedBranch.Id)
            {
                throw new InvalidOperationException(
                    "The selected division does not belong to the selected branch.");
            }

            branch = selectedBranch;
        }

        branch ??= activeBranches.SingleOrDefault(x => x.IsDefault)
            ?? throw new InvalidOperationException(
                "An active default branch is required before transactions can be posted.");
        division ??= branch.Divisions.SingleOrDefault(x => x.IsDefault)
            ?? throw new InvalidOperationException(
                "An active default division is required for the selected branch.");

        return new PostingDimension(branch.Id, division.Id);
    }

    private enum PostingAuthorization
    {
        Standard,
        ApprovedWorkflow,
        System
    }

    private sealed record PostingDimension(Guid BranchId, Guid DivisionId);
}
