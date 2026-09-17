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

    public async Task<BankDifferenceReport> GetDifferenceReportAsync(string userId, Guid organisationId, Guid sessionId, CancellationToken ct = default)
    {
        if (await access.FindAsync(userId, organisationId) is null)
            throw new UnauthorizedAccessException("You cannot view this organisation.");
        var session = await db.BankReconciliationSessions.AsNoTracking().Include(x => x.BankAccount)
            .SingleOrDefaultAsync(x => x.Id == sessionId && x.OrganisationId == organisationId, ct)
            ?? throw new InvalidOperationException("Reconciliation not found.");
        var statements = await db.BankStatementLines.AsNoTracking().Where(x => x.OrganisationId == organisationId &&
            x.BankAccountId == session.BankAccountId && x.TransactionDate >= session.StatementStartDate && x.TransactionDate <= session.StatementEndDate).ToListAsync(ct);
        var statementIds = statements.Select(x => x.Id).ToArray();
        var additional = await db.BankStatementAdditionalMatches.AsNoTracking().Where(x => statementIds.Contains(x.BankStatementLineId)).ToListAsync(ct);
        var matchedIds = statements.Where(x => x.MatchedPostedJournalLineId != null).Select(x => x.MatchedPostedJournalLineId!.Value)
            .Concat(additional.Select(x => x.PostedJournalLineId)).ToArray();
        var bankLines = await db.PostedJournalLines.AsNoTracking().Include(x => x.PostedJournal).Where(x =>
            x.PostedJournal.OrganisationId == organisationId && x.LedgerAccountId == session.BankAccountId &&
            (x.PostedJournal.EntryDate <= session.StatementEndDate || matchedIds.Contains(x.Id))).ToListAsync(ct);
        var excluded = await BankCodingHistory.UnmatchableJournalIdsAsync(db, organisationId, ct);
        var divisions = (await access.ListAccessibleBranchesAsync(userId, organisationId, ct)).SelectMany(x => x.Divisions).Select(x => x.Id).ToHashSet();
        var payments = await db.SupplierPayments.AsNoTracking().Include(x => x.SupplierBill).Where(x => x.OrganisationId == organisationId &&
            x.BankAccountId == session.BankAccountId && x.DivisionId != null && divisions.Contains(x.DivisionId.Value)).ToListAsync(ct);
        var issues = new List<BankDifferenceIssue>();
        foreach (var statement in statements)
        {
            if (statement.ReconciledAt is null)
            {
                issues.Add(new(statement.TransactionDate, statement.Description, statement.Amount, "Statement transaction has not been matched or coded.", statement.Id, null, null));
                continue;
            }
            var ids = additional.Where(x => x.BankStatementLineId == statement.Id).Select(x => x.PostedJournalLineId)
                .Concat(statement.MatchedPostedJournalLineId is Guid first ? new[] { first } : Array.Empty<Guid>()).ToHashSet();
            var lines = bankLines.Where(x => ids.Contains(x.Id)).ToList();
            if (lines.Any(x => x.DivisionId == null || !divisions.Contains(x.DivisionId.Value))) continue;
            var bad = lines.Where(x => excluded.Contains(x.PostedJournalId)).ToList();
            if (bad.Count > 0)
            {
                foreach (var line in bad)
                {
                    var payment = payments.FirstOrDefault(x => x.PostedJournalId == line.PostedJournalId);
                    issues.Add(new(statement.TransactionDate, statement.Description, line.Debit - line.Credit,
                        "Matched to a reversed payment or entry. The row still says reconciled, but this match no longer represents an active payment. Remove the match and check the bill/payment history before matching again.",
                        statement.Id, payment?.SupplierBillId, payment?.SupplierBill.BillNumber));
                }
            }
            else if (lines.Count != ids.Count || ids.Count == 0 || Math.Round(lines.Sum(x => x.Debit - x.Credit) - statement.Amount, 2) != 0)
                issues.Add(new(statement.TransactionDate, statement.Description, statement.Amount, "The linked ledger amounts do not equal the bank transaction.", statement.Id, null, null));
            else if (lines.Any(x => x.PostedJournal.EntryDate < session.StatementStartDate || x.PostedJournal.EntryDate > session.StatementEndDate))
                issues.Add(new(statement.TransactionDate, statement.Description, statement.Amount, "Matched payment is dated outside this statement period. Check its payment date.", statement.Id, null, null));
        }
        var occupied = (await db.BankStatementLines.AsNoTracking().Where(x => x.OrganisationId == organisationId && x.MatchedPostedJournalLineId != null)
            .Select(x => x.MatchedPostedJournalLineId!.Value).ToListAsync(ct)).ToHashSet();
        occupied.UnionWith(await db.BankStatementAdditionalMatches.AsNoTracking().Where(x => x.BankStatementLine.OrganisationId == organisationId).Select(x => x.PostedJournalLineId).ToListAsync(ct));
        foreach (var payment in payments.Where(x => x.PaymentDate >= session.StatementStartDate && x.PaymentDate <= session.StatementEndDate && !excluded.Contains(x.PostedJournalId)))
        {
            var line = bankLines.FirstOrDefault(x => x.PostedJournalId == payment.PostedJournalId);
            if (line is not null && !occupied.Contains(line.Id))
                issues.Add(new(payment.PaymentDate, payment.SupplierBill.BillNumber + " · " + payment.SupplierBill.SupplierReference,
                    line.Debit - line.Credit, "Bill payment is in the ledger but is not matched to a bank statement transaction.", null, payment.SupplierBillId, payment.SupplierBill.BillNumber));
        }
        var ledger = bankLines.Where(x => x.PostedJournal.EntryDate <= session.StatementEndDate).Sum(x => x.Debit - x.Credit);
        var opening = bankLines.Where(x => x.PostedJournal.EntryDate < session.StatementStartDate).Sum(x => x.Debit - x.Credit);
        return new(session, ledger, opening, statements.Sum(x => x.Amount), issues.OrderBy(x => x.Date).ToList());
    }

    private static AuditEvent Audit(Guid organisationId, string userId, string eventType, Guid entityId, object data) => new() { OrganisationId = organisationId, UserId = userId, EventType = eventType, EntityType = nameof(BankStatementLine), EntityId = entityId.ToString(), JsonData = JsonSerializer.Serialize(data) };
}

public sealed record BankDifferenceIssue(DateOnly Date, string Description, decimal Amount, string Reason, Guid? StatementId, Guid? BillId, string? BillNumber);
public sealed record BankDifferenceReport(BankReconciliationSession Session, decimal LedgerBalance, decimal OpeningLedgerBalance, decimal StatementMovement, IReadOnlyList<BankDifferenceIssue> Issues)
{
    public decimal Difference => Math.Round(Session.ClosingStatementBalance - LedgerBalance, 2);
}
