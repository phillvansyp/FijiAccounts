using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class PaymentDocumentPolicyTests
{
    [Fact]
    public async Task SupplierPaymentLinksBillAndStatementWithPolicyEnabled()
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        t.Organisation.RequireTradePaymentDocuments = true;
        await t.Db.SaveChangesAsync();
        var date = new DateOnly(2026, 8, 18);
        var bill = await t.Purchasing.PostBillAsync(t.UserId, new(t.Organisation.Id, t.Supplier.Id,
            "SUP-123", date, date.AddDays(30),
            [new("Supplies", 1m, 100m, VatTreatment.OutOfScope, t.Account("6500").Id)]));
        var bank = t.Account("1000");
        var s = await t.Reconciliation.AddStatementLineAsync(t.UserId,
            new(t.Organisation.Id, bank.Id, date, "Supplier payment", "SUP-123", -bill.Total));
        var payment = await t.Purchasing.PayBillAsync(t.UserId,
            new(t.Organisation.Id, bill.Id, date, "SUP-123", bill.Total, bank.Id, StatementLineId: s.Id));
        var saved = await t.Db.BankStatementLines.AsNoTracking().SingleAsync(x => x.Id == s.Id);
        var line = await t.Db.PostedJournalLines.SingleAsync(x => x.Id == saved.MatchedPostedJournalLineId);
        Assert.Equal(payment.PostedJournalId, line.PostedJournalId);
        Assert.Equal(BillStatus.Paid, (await t.Db.SupplierBills.AsNoTracking().SingleAsync(x => x.Id == bill.Id)).Status);
        Assert.Equal(0m, await t.AccountBalanceAsync("2000"));
    }

    [Theory]
    [InlineData("4000", 100)]
    [InlineData("6500", -100)]
    [InlineData("6000", -100)]
    [InlineData("2000", -100)]
    [InlineData("1100", 100)]
    public async Task MissingDocumentsDoNotBlockDirectCoding(string code, int amount)
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        t.Organisation.RequireTradePaymentDocuments = true;
        await t.Db.SaveChangesAsync();
        var s = await t.Reconciliation.AddStatementLineAsync(t.UserId,
            new(t.Organisation.Id, t.Account("1000").Id, new(2026, 8, 18), "Payment", "TEST", amount));
        var before = await t.Db.PostedJournals.CountAsync();
        await t.BankCoding.PostAndReconcileAsync(t.UserId,
            new(t.Organisation.Id, s.Id, code, "Payment", VatTreatment.OutOfScope));
        Assert.Equal(before + 1, await t.Db.PostedJournals.CountAsync());
        Assert.NotNull((await t.Db.BankStatementLines.AsNoTracking().SingleAsync(x => x.Id == s.Id)).ReconciledAt);
    }

    [Fact]
    public async Task StatementSupportsBankFeesWithoutCreatingArtificialBill()
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        t.Organisation.RequireTradePaymentDocuments = true;
        await t.Db.SaveChangesAsync();
        var s = await t.Reconciliation.AddStatementLineAsync(t.UserId,
            new(t.Organisation.Id, t.Account("1000").Id, new(2026, 8, 18), "Maintenance fee", "FEE", -5m));
        await t.BankCoding.PostAndReconcileAsync(t.UserId,
            new(t.Organisation.Id, s.Id, "6400", "Maintenance fee", VatTreatment.OutOfScope));
        Assert.NotNull((await t.Db.BankStatementLines.AsNoTracking().SingleAsync(x => x.Id == s.Id)).ReconciledAt);
        Assert.Equal(-5m, await t.AccountBalanceAsync("1000"));
    }

    [Fact]
    public async Task MissingInvoiceDoesNotBlockMatchingExistingJournal()
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        var bank = t.Account("1000");
        var date = new DateOnly(2026, 8, 18);
        var journal = await t.Posting.PostAsync(t.UserId, new(t.Organisation.Id, date, "OLD", "Legacy sales",
            [new(bank.Id, "Bank", 100m, 0m), new(t.Account("4000").Id, "Sales", 0m, 100m)]));
        var s = await t.Reconciliation.AddStatementLineAsync(t.UserId, new(t.Organisation.Id, bank.Id, date, "Customer", "OLD", 100m));
        t.Organisation.RequireTradePaymentDocuments = true;
        await t.Db.SaveChangesAsync();
        await t.Reconciliation.ReconcileAsync(t.UserId,
            t.Organisation.Id, s.Id, journal.Lines.Single(x => x.LedgerAccountId == bank.Id).Id);
        Assert.NotNull((await t.Db.BankStatementLines.AsNoTracking().SingleAsync(x => x.Id == s.Id)).ReconciledAt);
    }
}
