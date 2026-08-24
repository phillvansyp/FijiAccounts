using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SupplierBillStatementReconciliationTests
{
    [Fact]
    public async Task PayBillAsync_WithStatementLine_PaysAndReconcilesAtomically()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var bank = test.Account("1000");
        var paymentDate = new DateOnly(2026, 7, 15);
        var bill = await test.Purchasing.PostBillAsync(
            test.UserId,
            new SupplierBillRequest(
                test.Organisation.Id,
                test.Supplier.Id,
                "93091703",
                new DateOnly(2026, 6, 30),
                new DateOnly(2026, 7, 7),
                [
                    new SupplierBillLineRequest(
                        "Contract",
                        1m,
                        236.01m,
                        VatTreatment.Standard,
                        test.Account("6500").Id)
                ]));
        var statement = await test.Reconciliation.AddStatementLineAsync(
            test.UserId,
            new StatementLineRequest(
                test.Organisation.Id,
                bank.Id,
                paymentDate,
                "rentokil INV93091703",
                null,
                -bill.Total));

        var payment = await test.Purchasing.PayBillAsync(
            test.UserId,
            new SupplierPaymentRequest(
                test.Organisation.Id,
                bill.Id,
                paymentDate,
                bill.SupplierReference,
                bill.Total,
                bank.Id,
                statement.Id));

        var paidBill = await test.Db.SupplierBills.AsNoTracking().SingleAsync(x => x.Id == bill.Id);
        var reconciled = await test.Db.BankStatementLines.AsNoTracking().SingleAsync(x => x.Id == statement.Id);
        var bankJournalLine = await test.Db.PostedJournalLines.AsNoTracking().SingleAsync(
            x => x.PostedJournalId == payment.PostedJournalId && x.LedgerAccountId == bank.Id);

        Assert.Equal(BillStatus.Paid, paidBill.Status);
        Assert.Equal(bill.Total, paidBill.AmountPaid);
        Assert.NotNull(reconciled.ReconciledAt);
        Assert.Equal(test.UserId, reconciled.ReconciledByUserId);
        Assert.Equal(bankJournalLine.Id, reconciled.MatchedPostedJournalLineId);
        Assert.Equal(-bill.Total, await test.AccountBalanceAsync("1000"));
        Assert.Equal(0m, await test.AccountBalanceAsync("2000"));
    }

    [Fact]
    public async Task PayBillAsync_WithStatementLineAndDifferentAmount_IsRejectedBeforePosting()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var bank = test.Account("1000");
        var paymentDate = new DateOnly(2026, 7, 15);
        var bill = await test.Purchasing.PostBillAsync(
            test.UserId,
            new SupplierBillRequest(
                test.Organisation.Id,
                test.Supplier.Id,
                "93091703",
                new DateOnly(2026, 6, 30),
                new DateOnly(2026, 7, 7),
                [
                    new SupplierBillLineRequest(
                        "Contract",
                        1m,
                        100m,
                        VatTreatment.Standard,
                        test.Account("6500").Id)
                ]));
        var statement = await test.Reconciliation.AddStatementLineAsync(
            test.UserId,
            new StatementLineRequest(
                test.Organisation.Id,
                bank.Id,
                paymentDate,
                "rentokil INV93091703",
                null,
                -bill.Total));
        var journalCount = await test.Db.PostedJournals.CountAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            test.Purchasing.PayBillAsync(
                test.UserId,
                new SupplierPaymentRequest(
                    test.Organisation.Id,
                    bill.Id,
                    paymentDate,
                    bill.SupplierReference,
                    bill.Total - 1m,
                    bank.Id,
                    statement.Id)));

        Assert.Contains("exactly match", error.Message);
        Assert.Equal(journalCount, await test.Db.PostedJournals.CountAsync());
        Assert.Empty(await test.Db.SupplierPayments.AsNoTracking().ToListAsync());
        var unchanged = await test.Db.BankStatementLines.AsNoTracking().SingleAsync(x => x.Id == statement.Id);
        Assert.Null(unchanged.ReconciledAt);
        Assert.Null(unchanged.MatchedPostedJournalLineId);
    }
}
