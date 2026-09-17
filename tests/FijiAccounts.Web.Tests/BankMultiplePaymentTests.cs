using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class BankMultiplePaymentTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CombinedMatchDoesNotPostAgain_AndRemovingEitherPaymentReleasesWholeMatch(bool reverse)
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        var date = new DateOnly(2026, 7, 8);
        var first = await Payment(t, date, 3750.23m);
        var second = await Payment(t, date.AddDays(3), 281.25m);
        var statement = await Statement(t, date, -4031.48m);
        var before = await t.Db.PostedJournals.CountAsync();
        await t.Reconciliation.ReconcileManyAsync(t.UserId, t.Organisation.Id, statement.Id, [first.line, second.line]);
        Assert.Equal(before, await t.Db.PostedJournals.CountAsync());
        Assert.Equal(-4031.48m, await t.AccountBalanceAsync("1000"));
        Assert.Single(await t.Db.BankStatementAdditionalMatches.ToListAsync());
        var other = await Statement(t, date, -281.25m);
        await Assert.ThrowsAsync<InvalidOperationException>(() => t.Reconciliation.ReconcileAsync(t.UserId, t.Organisation.Id, other.Id, second.line));
        if (reverse)
            await t.Purchasing.ReversePaymentAsync(t.UserId, t.Organisation.Id, second.payment.Id, date.AddDays(3), "Wrong payment");
        else
            Assert.False(await t.BankCoding.ReopenCodingAsync(t.UserId, t.Organisation.Id, statement.Id));
        Assert.Empty(await t.Db.BankStatementAdditionalMatches.ToListAsync());
        Assert.Null((await t.Db.BankStatementLines.AsNoTracking().SingleAsync(x => x.Id == statement.Id)).ReconciledAt);
        if (reverse)
            await Assert.ThrowsAsync<InvalidOperationException>(() => t.Reconciliation.ReconcileAsync(t.UserId, t.Organisation.Id, other.Id, second.line));
        else
            await t.Reconciliation.ReconcileManyAsync(t.UserId, t.Organisation.Id, statement.Id, [second.line, first.line]);
    }

    [Theory]
    [InlineData("total")]
    [InlineData("duplicate")]
    [InlineData("month")]
    [InlineData("completed")]
    [InlineData("reversed")]
    [InlineData("account")]
    public async Task InvalidCombinedMatchIsRejectedWithoutPartialChanges(string fault)
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        var date = new DateOnly(2026, 7, 8);
        var first = await Payment(t, date, 100m);
        var second = await Payment(t, fault == "month" ? date.AddMonths(-1) : date, 20m);
        var statement = await Statement(t, date, fault == "total" ? -120.01m : -120m);
        if (fault == "completed")
        {
            t.Db.BankReconciliationSessions.Add(new() { OrganisationId = t.Organisation.Id, BankAccountId = t.Account("1000").Id,
                StatementStartDate = date, StatementEndDate = date, IsCompleted = true, CreatedByUserId = t.UserId });
            await t.Db.SaveChangesAsync();
        }
        if (fault == "reversed") await t.Purchasing.ReversePaymentAsync(t.UserId, t.Organisation.Id, second.payment.Id, date, "Incorrect");
        if (fault == "account") { statement.BankAccountId = t.Account("6500").Id; await t.Db.SaveChangesAsync(); }
        var before = await t.Db.PostedJournals.CountAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => t.Reconciliation.ReconcileManyAsync(t.UserId, t.Organisation.Id, statement.Id,
            [first.line, fault == "duplicate" ? first.line : second.line]));
        Assert.Empty(await t.Db.BankStatementAdditionalMatches.ToListAsync());
        Assert.Null((await t.Db.BankStatementLines.AsNoTracking().SingleAsync(x => x.Id == statement.Id)).ReconciledAt);
        Assert.Equal(before, await t.Db.PostedJournals.CountAsync());
    }

    private static async Task<(SupplierPayment payment, Guid line)> Payment(AccountingTestDatabase t, DateOnly date, decimal amount)
    {
        var bill = await t.Purchasing.PostBillAsync(t.UserId, new SupplierBillRequest(t.Organisation.Id, t.Supplier.Id, Guid.NewGuid().ToString(), date, date,
            [new SupplierBillLineRequest("Service", 1m, amount, VatTreatment.OutOfScope, t.Account("6500").Id)]));
        var payment = await t.Purchasing.PayBillAsync(t.UserId, new SupplierPaymentRequest(t.Organisation.Id, bill.Id, date, "Payment", amount, t.Account("1000").Id));
        var journal = await t.LoadJournalAsync(payment.PostedJournalId);
        return (payment, journal.Lines.Single(x => x.LedgerAccountId == t.Account("1000").Id).Id);
    }
    private static Task<BankStatementLine> Statement(AccountingTestDatabase t, DateOnly date, decimal amount) =>
        t.Reconciliation.AddStatementLineAsync(t.UserId, new StatementLineRequest(t.Organisation.Id, t.Account("1000").Id, date, "Combined payment", null, amount));
}
