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

    public async Task ReconcileAsync(
    string userId,
    Guid organisationId,
    Guid statementLineId,
    Guid journalLineId,
    CancellationToken ct = default)
{
    if (!await access.CanPostJournalsAsync(
            userId,
            organisationId))
    {
        throw new UnauthorizedAccessException(
            "You cannot reconcile this organisation.");
    }

    var statement =
        await db.BankStatementLines
            .SingleOrDefaultAsync(
                x =>
                    x.Id == statementLineId &&
                    x.OrganisationId == organisationId,
                ct)
        ?? throw new InvalidOperationException(
            "Statement line not found.");

    var completedReconciliationExists =
    await IsInsideCompletedReconciliationAsync(
        organisationId,
        statement.BankAccountId,
        statement.TransactionDate,
        ct);

    if (completedReconciliationExists)
    {
        throw new InvalidOperationException(
            "A statement line inside a completed reconciliation period cannot be changed.");
    }

    if (statement.ReconciledAt is not null)
    {
        throw new InvalidOperationException(
            "This statement line is already reconciled.");
    }

    var ledger =
        await db.PostedJournalLines
            .Include(x => x.PostedJournal)
            .SingleOrDefaultAsync(
                x =>
                    x.Id == journalLineId &&
                    x.PostedJournal.OrganisationId == organisationId &&
                    x.LedgerAccountId == statement.BankAccountId,
                ct)
        ?? throw new InvalidOperationException(
            "Matching bank ledger entry not found.");

    if (await db.BankStatementLines.AnyAsync(
            x => x.MatchedPostedJournalLineId == journalLineId,
            ct))
    {
        throw new InvalidOperationException(
            "That ledger entry is already reconciled.");
    }

    var ledgerAmount =
        Math.Round(
            ledger.Debit - ledger.Credit,
            2,
            MidpointRounding.AwayFromZero);

    var statementAmount =
        Math.Round(
            statement.Amount,
            2,
            MidpointRounding.AwayFromZero);

    if (Math.Abs(statementAmount - ledgerAmount) >
        AmountTolerance)
    {
        throw new InvalidOperationException(
            $"Statement and ledger amounts do not match exactly " +
            $"(statement: {statementAmount:N2}, ledger: {ledgerAmount:N2}).");
    }

    statement.MatchedPostedJournalLineId =
        ledger.Id;

    statement.ReconciledAt =
        DateTimeOffset.UtcNow;

    statement.ReconciledByUserId =
        userId;

    db.AuditEvents.Add(
        Audit(
            organisationId,
            userId,
            "BankStatementLineReconciled",
            statement.Id,
            new
            {
                JournalLineId = ledger.Id,
                statement.Amount
            }));

    await db.SaveChangesAsync(ct);
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
                    Reason = reason.Trim()
                }));
        await db.SaveChangesAsync(ct);
    }

    private static AuditEvent Audit(Guid organisationId, string userId, string eventType, Guid entityId, object data) => new() { OrganisationId = organisationId, UserId = userId, EventType = eventType, EntityType = nameof(BankStatementLine), EntityId = entityId.ToString(), JsonData = JsonSerializer.Serialize(data) };
}
