using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class BankDifferenceTests
{
    [Fact]
    public async Task StaleReversedMatchIdentifiesTheBillEvenWithNoUnmatchedStatementRows()
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        var date = new DateOnly(2026, 7, 15);
        var bank = t.Account("1000");
        var bill = await t.Purchasing.PostBillAsync(t.UserId, new SupplierBillRequest(t.Organisation.Id, t.Supplier.Id, "INV-1", date, date,
            [new SupplierBillLineRequest("Service", 1m, 135.95m, VatTreatment.OutOfScope, t.Account("6500").Id)]));
        var payment = await t.Purchasing.PayBillAsync(t.UserId, new SupplierPaymentRequest(t.Organisation.Id, bill.Id, date, "INV-1", 135.95m, bank.Id));
        var line = (await t.LoadJournalAsync(payment.PostedJournalId)).Lines.Single(x => x.LedgerAccountId == bank.Id);
        await t.Purchasing.ReversePaymentAsync(t.UserId, t.Organisation.Id, payment.Id, date, "Correct payment date");
        var statement = new BankStatementLine { OrganisationId = t.Organisation.Id, BankAccountId = bank.Id, TransactionDate = date,
            Description = "Supplier INV-1", Amount = -135.95m, MatchedPostedJournalLineId = line.Id, ReconciledAt = DateTimeOffset.UtcNow };
        var session = new BankReconciliationSession { OrganisationId = t.Organisation.Id, BankAccountId = bank.Id,
            StatementStartDate = new(2026,7,1), StatementEndDate = new(2026,7,31), ClosingStatementBalance = -135.95m, CreatedByUserId = t.UserId };
        t.Db.AddRange(statement, session); await t.Db.SaveChangesAsync();
        var report = await t.Reconciliation.GetDifferenceReportAsync(t.UserId, t.Organisation.Id, session.Id);
        Assert.Equal(-135.95m, report.Difference);
        var issue = Assert.Single(report.Issues);
        Assert.Equal(bill.Id, issue.BillId);
        Assert.Equal(statement.Id, issue.StatementId);
        Assert.Contains("reversed", issue.Reason);
        Assert.NotNull((await t.Db.BankStatementLines.SingleAsync(x => x.Id == statement.Id)).ReconciledAt);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => t.Reconciliation.GetDifferenceReportAsync("unknown", t.Organisation.Id, session.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => t.Reconciliation.GetDifferenceReportAsync(t.UserId, t.Organisation.Id, Guid.NewGuid()));
    }
}
