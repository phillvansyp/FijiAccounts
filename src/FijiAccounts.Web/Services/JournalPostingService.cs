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
    Guid? DivisionId = null);

public sealed record JournalPostRequest(
    Guid OrganisationId,
    DateOnly Date,
    string Reference,
    string? Description,
    IReadOnlyList<JournalLineInput> Lines,
    Guid? BranchId = null,
    Guid? DivisionId = null);

public sealed class JournalPostingService(
    ApplicationDbContext db,
    TenantAccessService tenantAccess,
    BankReconciliationService reconciliation)
{
    internal Task<PostedJournal> PostAutomaticallyAsync(
        JournalPostRequest request,
        CancellationToken cancellationToken = default)
    {
        return PostAsync(
            "system",
            request,
            cancellationToken,
            skipPermissionCheck: true);
    }

    public async Task<PostedJournal> PostAsync(
        string userId,
        JournalPostRequest request,
        CancellationToken cancellationToken = default,
        bool skipPermissionCheck = false)
    {
        if (!skipPermissionCheck &&
            !await tenantAccess.CanPostJournalsAsync(
                userId,
                request.OrganisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot post journals for this organisation.");
        }
        if (await db.AccountingPeriods.AnyAsync(x => x.OrganisationId == request.OrganisationId && x.IsLocked && request.Date >= x.StartsOn && request.Date <= x.EndsOn, cancellationToken)) throw new InvalidOperationException("The accounting period is locked.");

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
            Lines = request.Lines.Select((x, index) => new PostedJournalLine { LedgerAccountId = x.AccountId, BranchId = dimensions[index].BranchId, DivisionId = dimensions[index].DivisionId, Description = x.Description.Trim(), Debit = x.Debit, Credit = x.Credit }).ToList()
        };
        db.PostedJournals.Add(journal);
        db.AuditEvents.Add(new AuditEvent { OrganisationId = request.OrganisationId, EventType = "JournalPosted", EntityType = nameof(PostedJournal), EntityId = journal.Id.ToString(), UserId = userId, JsonData = JsonSerializer.Serialize(new { journal.SequenceNumber, journal.EntryDate, journal.Reference }) });
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

    private sealed record PostingDimension(Guid BranchId, Guid DivisionId);
}
