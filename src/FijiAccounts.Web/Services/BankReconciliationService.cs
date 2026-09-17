using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record StatementLineRequest(Guid OrganisationId, Guid BankAccountId, DateOnly Date, string Description, string? Reference, decimal Amount);

public sealed class BankReconciliationService(ApplicationDbContext db, TenantAccessService access)
{
    private const decimal AmountTolerance = 0.01m;

    public Task<bool> IsInsideCompletedReconciliationAsync(
    Guid organisationId,
    Guid bankAccountId,
    DateOnly transactionDate,
    CancellationToken ct = default)
{
    return db.BankReconciliationSessions.AnyAsync(
        x =>
            x.OrganisationId == organisationId &&
            x.BankAccountId == bankAccountId &&
            x.IsCompleted &&
            transactionDate >= x.StatementStartDate &&
            transactionDate <= x.StatementEndDate,
        ct);
}

    public async Task<BankStatementLine> AddStatementLineAsync(
    string userId,
    StatementLineRequest request,
    CancellationToken ct = default)
{
    if (!await access.CanPostJournalsAsync(
            userId,
            request.OrganisationId))
    {
        throw new UnauthorizedAccessException(
            "You cannot manage bank statements for this organisation.");
    }

    if (request.Amount == 0)
    {
        throw new InvalidOperationException(
            "A statement amount cannot be zero.");
    }

    if (!await db.LedgerAccounts.AnyAsync(
            x =>
                x.Id == request.BankAccountId &&
                x.OrganisationId == request.OrganisationId &&
                x.IsActive &&
                x.IsBankAccount,
            ct))
    {
        throw new InvalidOperationException(
            "Select an active bank account.");
    }

    var completedReconciliationExists =
    await IsInsideCompletedReconciliationAsync(
        request.OrganisationId,
        request.BankAccountId,
        request.Date,
        ct);

    if (completedReconciliationExists)
    {
        throw new InvalidOperationException(
            "A statement line cannot be added inside a completed reconciliation period.");
    }

    var line =
        new BankStatementLine
        {
            OrganisationId = request.OrganisationId,
            BankAccountId = request.BankAccountId,
            TransactionDate = request.Date,
            Description = request.Description.Trim(),
            Reference = request.Reference?.Trim(),
            Amount = request.Amount
        };

    db.BankStatementLines.Add(line);

    db.AuditEvents.Add(
        Audit(
            request.OrganisationId,
            userId,
            "BankStatementLineAdded",
            line.Id,
            new
            {
                request.Date,
                request.Amount,
                request.Reference
            }));

    await db.SaveChangesAsync(ct);

    return line;
}

    public Task ReconcileAsync(string userId, Guid organisationId, Guid statementLineId,
        Guid journalLineId, CancellationToken ct = default) =>
        ReconcileManyAsync(userId, organisationId, statementLineId, [journalLineId], ct);

    public async Task ReconcileManyAsync(string userId, Guid organisationId, Guid statementLineId,
        IReadOnlyCollection<Guid> journalLineIds, CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId))
            throw new UnauthorizedAccessException("You cannot reconcile this organisation.");
        if (journalLineIds.Count == 0 || journalLineIds.Count > 100 || journalLineIds.Distinct().Count() != journalLineIds.Count)
            throw new InvalidOperationException("Select each payment once (up to 100 payments).");
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        var statement = await db.BankStatementLines.SingleOrDefaultAsync(
            x => x.Id == statementLineId && x.OrganisationId == organisationId, ct)
            ?? throw new InvalidOperationException("Statement line not found.");
        if (await IsInsideCompletedReconciliationAsync(organisationId, statement.BankAccountId, statement.TransactionDate, ct))
            throw new InvalidOperationException("A statement line inside a completed reconciliation period cannot be changed.");
        if (statement.ReconciledAt is not null)
            throw new InvalidOperationException("This statement line is already reconciled.");
        var ids = journalLineIds.ToArray();
        var lines = await db.PostedJournalLines.Include(x => x.PostedJournal).Where(x => ids.Contains(x.Id) &&
            x.PostedJournal.OrganisationId == organisationId && x.LedgerAccountId == statement.BankAccountId).ToListAsync(ct);
        if (lines.Count != ids.Length)
            throw new InvalidOperationException("Matching bank ledger entry not found.");
        var excluded = await BankCodingHistory.UnmatchableJournalIdsAsync(db, organisationId, ct);
        if (lines.Any(x => excluded.Contains(x.PostedJournalId) || x.PostedJournal.Reference.StartsWith("REV-BANK-", StringComparison.Ordinal)))
            throw new InvalidOperationException("This entry has been reversed and cannot be matched. Choose an active bank entry.");
        if (await db.BankStatementLines.AnyAsync(x => x.MatchedPostedJournalLineId != null && ids.Contains(x.MatchedPostedJournalLineId.Value), ct) ||
            await db.BankStatementAdditionalMatches.AnyAsync(x => ids.Contains(x.PostedJournalLineId), ct))
            throw new InvalidOperationException("That ledger entry is already reconciled.");
        if (ids.Length > 1)
        {
            var journalIds = lines.Select(x => x.PostedJournalId).ToArray();
            var paymentJournals = await db.SupplierPayments.Where(x => x.OrganisationId == organisationId && journalIds.Contains(x.PostedJournalId))
                .Select(x => x.PostedJournalId).ToListAsync(ct);
            var divisions = (await access.ListAccessibleBranchesAsync(userId, organisationId)).SelectMany(x => x.Divisions).Select(x => x.Id).ToHashSet();
            if (lines.Any(x => !paymentJournals.Contains(x.PostedJournalId) || x.DivisionId == null || !divisions.Contains(x.DivisionId.Value)))
                throw new InvalidOperationException("Select accessible supplier bill payments from this bank account.");
            if (lines.Any(x => x.PostedJournal.EntryDate.Year != statement.TransactionDate.Year || x.PostedJournal.EntryDate.Month != statement.TransactionDate.Month))
                throw new InvalidOperationException("The selected payment dates must be in the same month as this statement transaction. Correct the payment dates first.");
            if (statement.Amount >= 0 || lines.Any(x => x.Debit - x.Credit >= 0))
                throw new InvalidOperationException("Select outgoing bill payments for an outgoing bank transaction.");
        }
        var ledgerAmount = Math.Round(lines.Sum(x => x.Debit - x.Credit), 2, MidpointRounding.AwayFromZero);
        var statementAmount = Math.Round(statement.Amount, 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(statementAmount - ledgerAmount) > (ids.Length > 1 ? 0m : AmountTolerance))
            throw new InvalidOperationException($"Statement and ledger amounts do not match exactly (statement: {statementAmount:N2}, ledger: {ledgerAmount:N2}).");
        statement.MatchedPostedJournalLineId = ids[0];
        foreach (var id in ids.Skip(1))
            db.BankStatementAdditionalMatches.Add(new() { BankStatementLineId = statement.Id, PostedJournalLineId = id });
        statement.ReconciledAt = DateTimeOffset.UtcNow;
        statement.ReconciledByUserId = userId;
        db.AuditEvents.Add(Audit(organisationId, userId, "BankStatementLineReconciled", statement.Id,
            new { JournalLineId = ids[0], JournalLineIds = ids, statement.Amount }));
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
    }

    public async Task UnreconcileAsync(
        string userId,
        Guid organisationId,
        Guid statementLineId,
        string reason,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot change reconciliations for this organisation.");
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException(
                "Enter a reason for removing the reconciliation match.");
        }

        var statement = await db.BankStatementLines.SingleOrDefaultAsync(
            x => x.Id == statementLineId && x.OrganisationId == organisationId,
            ct) ?? throw new InvalidOperationException("Statement line not found.");
        if (statement.ReconciledAt is null || statement.MatchedPostedJournalLineId is null)
        {
            throw new InvalidOperationException("This statement line is not reconciled.");
        }
        if (await IsInsideCompletedReconciliationAsync(
                organisationId,
                statement.BankAccountId,
                statement.TransactionDate,
                ct))
        {
            throw new InvalidOperationException(
                "A statement line inside a completed reconciliation period cannot be changed.");
        }

        var additional = await db.BankStatementAdditionalMatches.Where(x => x.BankStatementLineId == statement.Id).ToListAsync(ct);
        var previousJournalLineIds = additional.Select(x => x.PostedJournalLineId).Prepend(statement.MatchedPostedJournalLineId.Value).ToArray();
        db.BankStatementAdditionalMatches.RemoveRange(additional);
        var previousJournalLineId = statement.MatchedPostedJournalLineId.Value;
        statement.MatchedPostedJournalLineId = null;
        statement.ReconciledAt = null;
        statement.ReconciledByUserId = null;
        db.AuditEvents.Add(
            Audit(
                organisationId,
                userId,
                "BankStatementLineUnreconciled",
                statement.Id,
                new
                {
                    PreviousJournalLineId = previousJournalLineId,
                    PreviousJournalLineIds = previousJournalLineIds,
                    Reason = reason.Trim()
                }));
        await db.SaveChangesAsync(ct);
    }

    private static AuditEvent Audit(Guid organisationId, string userId, string eventType, Guid entityId, object data) => new() { OrganisationId = organisationId, UserId = userId, EventType = eventType, EntityType = nameof(BankStatementLine), EntityId = entityId.ToString(), JsonData = JsonSerializer.Serialize(data) };
}
