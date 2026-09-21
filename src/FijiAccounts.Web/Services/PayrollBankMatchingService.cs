using System.Text.Json;
using System.Text.RegularExpressions;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed class PayrollBankMatchingService(ApplicationDbContext db, TenantAccessService access,
    JournalPostingService posting, BankReconciliationService reconciliation)
{
    // Both sides must have a unique candidate. Amount alone is never an employee identity.
    public static bool Matches(PayrollEmployeeDetail employee, BankStatementLine statement) =>
        employee.NetPay > 0 && statement.Amount == -employee.NetPay && statement.TransactionDate == employee.PaymentDate &&
        (ContainsIdentity(statement.Description + " " + statement.Reference, employee.EmployeeId) ||
         (employee.Name != employee.EmployeeId && ContainsIdentity(statement.Description + " " + statement.Reference, employee.Name)));

    private static bool ContainsIdentity(string text, string identity)
    {
        static string Normalise(string value) => Regex.Replace(value.ToUpperInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
        var key = Normalise(identity);
        return key.Length >= 4 && (" " + Normalise(text) + " ").Contains(" " + key + " ", StringComparison.Ordinal);
    }

    public async Task<int> MatchAsync(string userId, Guid organisationId, CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId) ||
            await access.GetReportDivisionScopeAsync(userId, organisationId, ct) is not null) return 0;
        var organisation = await access.FindAsync(userId, organisationId);
        if (organisation is null) return 0;
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        var runs = await db.PayrollIslandPayRunImports.AsNoTracking().Include(x => x.Connection)
            .Where(x => x.OrganisationId == organisationId && x.Status == PayrollIslandImportStatus.Posted &&
                x.PostedJournalId != null && x.Connection.IsActive && x.EmployeesJson != null &&
                !db.PayrollIslandPayRunImports.Any(newer => newer.ConnectionId == x.ConnectionId &&
                    newer.ExternalPayRunId == x.ExternalPayRunId && newer.Revision > x.Revision)).ToListAsync(ct);
        var links = await db.PayrollBankMatches.AsNoTracking().Where(x => x.OrganisationId == organisationId).ToListAsync(ct);
        var employees = runs.Where(x => x.Currency == organisation.Organisation.BaseCurrency)
            .SelectMany(run => PayrollEmployeeDetail.Read(run).Select(employee => new { Run = run, Employee = employee }))
            .Where(x => !links.Any(l => l.ConnectionId == x.Run.ConnectionId && l.ExternalPaymentId == x.Employee.PaymentId)).ToList();
        var statements = await db.BankStatementLines.Where(x => x.OrganisationId == organisationId &&
            x.Amount < 0 && (x.ReconciledAt != null || x.MatchedPostedJournalLineId == null) &&
            x.BankAccount.IsActive).ToListAsync(ct);
        var closed = await db.BankReconciliationSessions.AsNoTracking().Where(x => x.OrganisationId == organisationId && x.IsCompleted).ToListAsync(ct);
        var locked = await db.AccountingPeriods.AsNoTracking().Where(x => x.OrganisationId == organisationId && x.IsLocked).ToListAsync(ct);
        statements = statements.Where(s => !links.Any(l => l.BankStatementLineId == s.Id) &&
            (s.ReconciledAt != null || (!closed.Any(p => p.BankAccountId == s.BankAccountId && s.TransactionDate >= p.StatementStartDate && s.TransactionDate <= p.StatementEndDate) &&
            !locked.Any(p => s.TransactionDate >= p.StartsOn && s.TransactionDate <= p.EndsOn)))).ToList();
        var candidates = employees.SelectMany(e => statements.Where(s => Matches(e.Employee, s))
            .Select(s => new { e.Run, e.Employee, Statement = s })).ToList();
        var excludedJournals = await BankCodingHistory.UnmatchableJournalIdsAsync(db, organisationId, ct);
        var matched = 0;
        foreach (var match in candidates.Where(c => candidates.Count(x => x.Statement.Id == c.Statement.Id) == 1 &&
                     candidates.Count(x => x.Run.ConnectionId == c.Run.ConnectionId && x.Employee.PaymentId == c.Employee.PaymentId) == 1))
        {
            var row = match.Statement;
            var amount = match.Employee.NetPay;
            var existing = await db.PostedJournalLines.Include(x => x.PostedJournal).ThenInclude(x => x.Lines)
                .Where(x => x.PostedJournal.OrganisationId == organisationId && x.LedgerAccountId == row.BankAccountId &&
                    x.PostedJournal.EntryDate == row.TransactionDate).ToListAsync(ct);
            existing = existing.Where(x => x.Debit - x.Credit == -amount && !excludedJournals.Contains(x.PostedJournalId) &&
                !x.PostedJournal.Reference.StartsWith("REV-BANK-", StringComparison.Ordinal) &&
                (ContainsIdentity(x.Description + " " + x.PostedJournal.Description + " " + x.PostedJournal.Reference, match.Employee.Name) ||
                 ContainsIdentity(x.Description + " " + x.PostedJournal.Reference, match.Employee.EmployeeId))).ToList();
            PostedJournal journal;
            if (existing.Count > 0)
            {
                if (existing.Count != 1) continue;
                var bankLine = existing[0];
                // Never add a second payment or reuse an expense-coded entry as a payroll liability payment.
                if (bankLine.PostedJournal.Lines.Count != 2 || !bankLine.PostedJournal.Lines.Any(x =>
                        x.LedgerAccountId == match.Run.Connection.NetWagesPayableAccountId && x.Debit == amount && x.Credit == 0) ||
                    (row.ReconciledAt != null && row.MatchedPostedJournalLineId != bankLine.Id) ||
                    await db.BankStatementLines.AnyAsync(x => x.Id != row.Id && x.MatchedPostedJournalLineId == bankLine.Id, ct) ||
                    await db.BankStatementAdditionalMatches.AnyAsync(x => x.PostedJournalLineId == bankLine.Id, ct)) continue;
                journal = bankLine.PostedJournal;
            }
            else
            {
                if (row.ReconciledAt != null) continue;
                journal = await posting.PostAsync(userId, new(organisationId, row.TransactionDate,
                $"PAY-BANK-{row.Id:N}", $"Payroll payment: {match.Employee.Name} · {match.Run.PayRunNumber}",
                [new(match.Run.Connection.NetWagesPayableAccountId, match.Employee.Name, amount, 0),
                 new(row.BankAccountId, match.Employee.Name, 0, amount)],
                Purpose: JournalPurpose.Payroll, Currency: match.Run.Currency), ct);
            }
            if (row.ReconciledAt == null)
                await reconciliation.ReconcileAsync(userId, organisationId, row.Id,
                    journal.Lines.Single(x => x.LedgerAccountId == row.BankAccountId).Id, ct);
            var link = new PayrollBankMatch { OrganisationId = organisationId, ConnectionId = match.Run.ConnectionId,
                ExternalPaymentId = match.Employee.PaymentId, PayRunImportId = match.Run.Id,
                BankStatementLineId = row.Id, PostedJournalId = journal.Id, Amount = amount };
            db.PayrollBankMatches.Add(link);
            db.AuditEvents.Add(new AuditEvent { OrganisationId = organisationId, UserId = userId,
                EventType = "PayrollPaymentAutomaticallyMatched", EntityType = nameof(PayrollBankMatch), EntityId = link.Id.ToString(),
                JsonData = JsonSerializer.Serialize(new { match.Run.Id, match.Run.Revision, match.Employee.PaymentId, StatementId = row.Id, amount }) });
            await db.SaveChangesAsync(ct);
            matched++;
        }
        if (transaction is not null) await transaction.CommitAsync(ct);
        return matched;
    }
}
