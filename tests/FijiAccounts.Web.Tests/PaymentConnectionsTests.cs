using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class PaymentConnectionsTests
{
    private static PaymentConnectionsService Service(AccountingTestDatabase t) => new(t.Db, t.Access, t.Purchasing, t.CustomerReceipts, t.BankCoding, t.Reconciliation);
    private static Task<SupplierBill> Bill(AccountingTestDatabase t, decimal amount) => t.Purchasing.PostBillAsync(t.UserId,
        new(t.Organisation.Id, t.Supplier.Id, Guid.NewGuid().ToString(), new(2026, 6, 1), new(2026, 6, 30),
            [new("Supplies", 1m, amount, VatTreatment.OutOfScope, t.Account("6500").Id)]));
    private static Task<BankStatementLine> Statement(AccountingTestDatabase t, decimal amount) => t.Reconciliation.AddStatementLineAsync(t.UserId,
        new(t.Organisation.Id, t.Account("1000").Id, new(2026, 7, 15), "Payment", "TEST", amount));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SuggestedForeignPaymentUsesRemainingDocumentBalanceAndActualBankAmount(bool sales)
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        Guid id;
        if (sales)
            id = (await t.SalesInvoices.CreateAndPostAsync(t.UserId, new(t.Organisation.Id, t.Customer.Id,
                new(2026, 6, 1), new(2026, 6, 30), [new("Services", 1, 100, VatTreatment.OutOfScope, t.Account("4000").Id)],
                Currency: "USD", ExchangeRateToBase: 2m))).Id;
        else
            id = (await t.Purchasing.PostBillAsync(t.UserId, new(t.Organisation.Id, t.Supplier.Id, "FX",
                new(2026, 6, 1), new(2026, 6, 30), [new("Supplies", 1, 100, VatTreatment.OutOfScope, t.Account("6500").Id)],
                Currency: "USD", ExchangeRateToBase: 2m))).Id;
        // Part-payment at a different exchange rate leaves USD 75 / FJD 150 to settle.
        var first = await Statement(t, sales ? 48 : -48);
        await Service(t).ConnectAsync(t.UserId, new(t.Organisation.Id, first.Id, null, [], [new(id, 48, 25)]));
        var graph = await Service(t).ReadAsync(t.UserId, t.Organisation.Id);
        var doc = graph.Documents.Single(d => d.Id == id);
        Assert.Equal(150, doc.Outstanding);
        Assert.Equal(75, doc.OutstandingDocumentAmount);
        var bank = await Statement(t, sales ? 145 : -145);
        await Service(t).ConnectAsync(t.UserId, new(t.Organisation.Id, bank.Id, null, [],
            [new(id, Math.Abs(bank.Amount), doc.OutstandingDocumentAmount)]));
        var result = await Service(t).ReadAsync(t.UserId, t.Organisation.Id);
        Assert.Equal(0, result.Documents.Single(d => d.Id == id).Outstanding);
        Assert.Single(result.Payments, p => p.StatementId == bank.Id);
        Assert.Equal(sales ? 193 : -193, await t.AccountBalanceAsync("1000"));
    }

    [Fact]
    public async Task SplitPaymentSupportsPartialBillsAndPreventsRepeatSave()
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        var a = await Bill(t, 100); var b = await Bill(t, 200); var s = await Statement(t, -150);
        var request = new ConnectPaymentsRequest(t.Organisation.Id, s.Id, null, [], [new(a.Id, 100), new(b.Id, 50)]);
        await Service(t).ConnectAsync(t.UserId, request);
        Assert.Equal(-150, await t.AccountBalanceAsync("1000"));
        Assert.Equal(300, await t.AccountBalanceAsync("6500"));
        Assert.Equal(2, await t.Db.SupplierPayments.CountAsync());
        Assert.Single(await t.Db.BankStatementAdditionalMatches.ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(t).ConnectAsync(t.UserId, request));
        Assert.Equal(2, await t.Db.SupplierPayments.CountAsync());
        var graph = await Service(t).ReadAsync(t.UserId, t.Organisation.Id);
        Assert.Equal(2, graph.Payments.Count(p => p.StatementId == s.Id));
        Assert.Equal(150, graph.Documents.Single(d => d.Id == b.Id).Outstanding);
    }

    [Fact]
    public async Task AttachingBillReplacesCodingWithoutDuplicateExpenseOrBankPayment()
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        var s = await Statement(t, -100);
        await t.BankCoding.PostAndReconcileAsync(t.UserId, new(t.Organisation.Id, s.Id, "6500", "Supplies", VatTreatment.OutOfScope));
        var b = await Bill(t, 100);
        var original = (await t.Db.BankStatementLines.AsNoTracking().SingleAsync()).MatchedPostedJournalLineId;
        await Service(t).ConnectAsync(t.UserId, new(t.Organisation.Id, s.Id, original, [], [new(b.Id, 100)]));
        Assert.Equal(100, await t.AccountBalanceAsync("6500"));
        Assert.Equal(-100, await t.AccountBalanceAsync("1000"));
        Assert.Equal(0, await t.AccountBalanceAsync("2000"));
    }

    [Fact]
    public async Task LaterAllocationFailureRollsBackCodingReversalAndEarlierPayment()
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        var a = await Bill(t, 100); var b = await Bill(t, 20); var s = await Statement(t, -150);
        await t.BankCoding.PostAndReconcileAsync(t.UserId, new(t.Organisation.Id, s.Id, "6500", "Supplies", VatTreatment.OutOfScope));
        var original = (await t.Db.BankStatementLines.AsNoTracking().SingleAsync()).MatchedPostedJournalLineId;
        var journals = await t.Db.PostedJournals.CountAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(t).ConnectAsync(t.UserId,
            new(t.Organisation.Id, s.Id, original, [], [new(a.Id, 100), new(b.Id, 50)])));
        Assert.Equal(journals, await t.Db.PostedJournals.CountAsync());
        Assert.Empty(await t.Db.SupplierPayments.ToListAsync());
        Assert.Equal(original, (await t.Db.BankStatementLines.AsNoTracking().SingleAsync()).MatchedPostedJournalLineId);
        Assert.Equal(-150, await t.AccountBalanceAsync("1000"));
    }

    [Fact]
    public async Task ExistingPaymentsAcrossMonthsAreReusedWithoutNewPosting()
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        var a = await Bill(t, 100); var b = await Bill(t, 50);
        await t.Purchasing.PayBillAsync(t.UserId, new(t.Organisation.Id, a.Id, new(2026, 6, 30), "A", 100, t.Account("1000").Id));
        await t.Purchasing.PayBillAsync(t.UserId, new(t.Organisation.Id, b.Id, new(2026, 7, 1), "B", 50, t.Account("1000").Id));
        var s = await Statement(t, -150); var svc = Service(t);
        var graph = await svc.ReadAsync(t.UserId, t.Organisation.Id);
        var journals = await t.Db.PostedJournals.CountAsync();
        await svc.ConnectAsync(t.UserId, new(t.Organisation.Id, s.Id, null, graph.Payments.Select(p => p.LineId).ToArray(), []));
        Assert.Equal(journals, await t.Db.PostedJournals.CountAsync());
        Assert.Equal(new DateOnly(2026, 6, 30), (await t.Db.SupplierPayments.AsNoTracking().OrderBy(p => p.PaymentDate).FirstAsync()).PaymentDate);
    }

    [Fact]
    public async Task CombinedCustomerReceiptLinksBothInvoices()
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        var ids = new List<Guid>();
        foreach (var amount in new[] { 100m, 50m })
            ids.Add((await t.SalesInvoices.CreateAndPostAsync(t.UserId, new(t.Organisation.Id, t.Customer.Id,
                new(2026, 6, 1), new(2026, 6, 30), [new("Services", 1, amount, VatTreatment.OutOfScope, t.Account("4000").Id)]))).Id);
        var s = await Statement(t, 150);
        await Service(t).ConnectAsync(t.UserId, new(t.Organisation.Id, s.Id, null, [], [new(ids[0], 100), new(ids[1], 50)]));
        Assert.Equal(150, await t.AccountBalanceAsync("1000"));
        Assert.Equal(0, await t.AccountBalanceAsync("1100"));
        Assert.Equal(2, await t.Db.CustomerReceipts.CountAsync());
        var additional = await t.Db.BankStatementAdditionalMatches.SingleAsync();
        var journalId = (await t.Db.PostedJournalLines.SingleAsync(x => x.Id == additional.PostedJournalLineId)).PostedJournalId;
        var receiptId = await t.Db.CustomerReceipts.Where(x => x.PostedJournalId == journalId).Select(x => x.Id).SingleAsync();
        await t.CustomerReceipts.ReverseAsync(t.UserId, t.Organisation.Id, receiptId, new(2026, 7, 16), "Incorrect receipt");
        Assert.Empty(await t.Db.BankStatementAdditionalMatches.ToListAsync());
        Assert.Null((await t.Db.BankStatementLines.AsNoTracking().SingleAsync()).ReconciledAt);
        Assert.DoesNotContain((await Service(t).ReadAsync(t.UserId, t.Organisation.Id)).Payments, p => p.LineId == additional.PostedJournalLineId);
    }

    [Fact]
    public async Task CompletedPeriodRemainsUnchangedWhenCorrectionIsRejected()
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        var b = await Bill(t, 100); var s = await Statement(t, -100);
        await t.BankCoding.PostAndReconcileAsync(t.UserId, new(t.Organisation.Id, s.Id, "6500", "Supplies", VatTreatment.OutOfScope));
        var original = (await t.Db.BankStatementLines.AsNoTracking().SingleAsync()).MatchedPostedJournalLineId;
        t.Db.BankReconciliationSessions.Add(new() { OrganisationId = t.Organisation.Id, BankAccountId = t.Account("1000").Id,
            StatementStartDate = new(2026, 7, 1), StatementEndDate = new(2026, 7, 31), IsCompleted = true, CreatedByUserId = t.UserId });
        await t.Db.SaveChangesAsync();
        var before = await t.Db.PostedJournals.CountAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(t).ConnectAsync(t.UserId, new(t.Organisation.Id, s.Id, original, [], [new(b.Id, 100)])));
        Assert.Equal(before, await t.Db.PostedJournals.CountAsync());
        Assert.Equal(original, (await t.Db.BankStatementLines.AsNoTracking().SingleAsync()).MatchedPostedJournalLineId);
        Assert.Empty(await t.Db.SupplierPayments.ToListAsync());
    }
}
