using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class CustomerReceiptStatementReconciliationTests
{
    [Fact]
    public async Task RecordAsync_WithStatementLine_ReceivesAndReconcilesAtomically()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var bank = test.Account("1000");
        var receiptDate = new DateOnly(2026, 7, 17);
        var invoice = await test.SalesInvoices.CreateAndPostAsync(
            test.UserId,
            new SalesInvoiceRequest(
                test.Organisation.Id,
                test.Customer.Id,
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 31),
                [
                    new SalesInvoiceLineRequest(
                        "Services",
                        1m,
                        200m,
                        VatTreatment.Standard,
                        test.Account("4000").Id)
                ]));
        var statement = await test.Reconciliation.AddStatementLineAsync(
            test.UserId,
            new StatementLineRequest(
                test.Organisation.Id,
                bank.Id,
                receiptDate,
                $"Customer payment {invoice.InvoiceNumber}",
                null,
                invoice.Total));

        var receipt = await test.CustomerReceipts.RecordAsync(
            test.UserId,
            new CustomerReceiptRequest(
                test.Organisation.Id,
                invoice.Id,
                receiptDate,
                invoice.InvoiceNumber,
                invoice.Total,
                bank.Id,
                statement.Id));

        var paidInvoice = await test.Db.SalesInvoices.AsNoTracking().SingleAsync(x => x.Id == invoice.Id);
        var reconciled = await test.Db.BankStatementLines.AsNoTracking().SingleAsync(x => x.Id == statement.Id);
        var bankJournalLine = await test.Db.PostedJournalLines.AsNoTracking().SingleAsync(
            x => x.PostedJournalId == receipt.PostedJournalId && x.LedgerAccountId == bank.Id);

        Assert.Equal(InvoiceStatus.Paid, paidInvoice.Status);
        Assert.Equal(invoice.Total, paidInvoice.AmountPaid);
        Assert.NotNull(reconciled.ReconciledAt);
        Assert.Equal(test.UserId, reconciled.ReconciledByUserId);
        Assert.Equal(bankJournalLine.Id, reconciled.MatchedPostedJournalLineId);
        Assert.Equal(invoice.Total, await test.AccountBalanceAsync("1000"));
        Assert.Equal(0m, await test.AccountBalanceAsync("1100"));
    }

    [Fact]
    public async Task RecordAsync_WithStatementLineAndDifferentAmount_IsRejectedBeforePosting()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var bank = test.Account("1000");
        var receiptDate = new DateOnly(2026, 7, 17);
        var invoice = await test.SalesInvoices.CreateAndPostAsync(
            test.UserId,
            new SalesInvoiceRequest(
                test.Organisation.Id,
                test.Customer.Id,
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 31),
                [
                    new SalesInvoiceLineRequest(
                        "Services",
                        1m,
                        100m,
                        VatTreatment.Standard,
                        test.Account("4000").Id)
                ]));
        var statement = await test.Reconciliation.AddStatementLineAsync(
            test.UserId,
            new StatementLineRequest(
                test.Organisation.Id,
                bank.Id,
                receiptDate,
                $"Customer payment {invoice.InvoiceNumber}",
                null,
                invoice.Total));
        var journalCount = await test.Db.PostedJournals.CountAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            test.CustomerReceipts.RecordAsync(
                test.UserId,
                new CustomerReceiptRequest(
                    test.Organisation.Id,
                    invoice.Id,
                    receiptDate,
                    invoice.InvoiceNumber,
                    invoice.Total - 1m,
                    bank.Id,
                    statement.Id)));

        Assert.Contains("exactly match", error.Message);
        Assert.Equal(journalCount, await test.Db.PostedJournals.CountAsync());
        Assert.Empty(await test.Db.CustomerReceipts.AsNoTracking().ToListAsync());
        var unchanged = await test.Db.BankStatementLines.AsNoTracking().SingleAsync(x => x.Id == statement.Id);
        Assert.Null(unchanged.ReconciledAt);
        Assert.Null(unchanged.MatchedPostedJournalLineId);
    }
}
