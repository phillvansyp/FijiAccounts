using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class BankPaymentEditTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EditRemovesOnlyMatch_ReverseRemovesMatchAndPayment(bool reversePayment)
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        var date = new DateOnly(2026, 3, 17);
        var bank = t.Account("1000");
        var bill = await t.Purchasing.PostBillAsync(t.UserId, new SupplierBillRequest(t.Organisation.Id,
            t.Supplier.Id, "RENT-1", date, date,
            [new SupplierBillLineRequest("Service", 1m, 78.26m, VatTreatment.OutOfScope, t.Account("6500").Id)]));
        var payment = await t.Purchasing.PayBillAsync(t.UserId,
            new SupplierPaymentRequest(t.Organisation.Id, bill.Id, date, "RENT-1", 78.26m, bank.Id));
        var journal = await t.LoadJournalAsync(payment.PostedJournalId);
        var bankLine = journal.Lines.Single(l => l.LedgerAccountId == bank.Id);
        var statement = new BankStatementLine { OrganisationId = t.Organisation.Id, BankAccountId = bank.Id,
            TransactionDate = date, Description = "Rentokil", Amount = -78.26m };
        t.Db.BankStatementLines.Add(statement);
        await t.Db.SaveChangesAsync();
        await t.Reconciliation.ReconcileAsync(t.UserId, t.Organisation.Id, statement.Id, bankLine.Id);
        var count = await t.Db.PostedJournals.CountAsync();

        if (reversePayment)
            await t.Purchasing.ReversePaymentAsync(t.UserId, t.Organisation.Id, payment.Id, date, "Wrong payment");
        else
            Assert.False(await t.BankCoding.ReopenCodingAsync(t.UserId, t.Organisation.Id, statement.Id));

        var saved = await t.Db.BankStatementLines.AsNoTracking().SingleAsync(x => x.Id == statement.Id);
        Assert.Null(saved.ReconciledAt);
        Assert.Null(saved.MatchedPostedJournalLineId);
        Assert.Equal(-78.26m, saved.Amount);
        Assert.Equal(count + (reversePayment ? 1 : 0), await t.Db.PostedJournals.CountAsync());
        Assert.Equal(reversePayment ? 0m : -78.26m, await t.AccountBalanceAsync("1000"));
        Assert.Equal(reversePayment ? 0m : 78.26m,
            (await t.Db.SupplierBills.AsNoTracking().SingleAsync(x => x.Id == bill.Id)).AmountPaid);
        Assert.True(await t.Db.AuditEvents.AnyAsync(x => x.EventType == "BankStatementLineUnreconciled"));
    }
}
