using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SalesInvoiceSettlementVoidTests
{
    [Fact]
    public async Task VoidAsync_RejectsInvoiceWithReceipt_EvenIfStatusIsPosted()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice =
            await test.SalesInvoices.CreateAndPostAsync(
                test.UserId,
                new SalesInvoiceRequest(
                    OrganisationId: test.Organisation.Id,
                    CustomerId: test.Customer.Id,
                    IssueDate: new DateOnly(2026, 8, 5),
                    DueDate: new DateOnly(2026, 9, 5),
                    Lines:
                    [
                        new SalesInvoiceLineRequest(
                            Description: "Receipt void integrity test",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id)
                    ]));

        var bank = test.Account("1000");

        var receipts =
            new CustomerReceiptService(
                test.Db,
                test.Access,
                test.Posting,
                test.Reconciliation,
                test.Notifications);

        await receipts.RecordAsync(
            test.UserId,
            new CustomerReceiptRequest(
                OrganisationId: test.Organisation.Id,
                SalesInvoiceId: invoice.Id,
                Date: new DateOnly(2026, 8, 8),
                Reference: "RCPT-VOID-001",
                Amount: 25m,
                BankAccountId: bank.Id));

        var settledInvoice =
            await test.Db.SalesInvoices
                .SingleAsync(x => x.Id == invoice.Id);

        Assert.Equal(25m, settledInvoice.AmountPaid);
        Assert.Equal(
            InvoiceStatus.PartPaid,
            settledInvoice.Status);

        // Simulate inconsistent persisted status. AmountPaid remains
        // authoritative evidence that the invoice has settlement history.
        settledInvoice.Status = InvoiceStatus.Posted;
        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                test.SalesInvoices.VoidAsync(
                    test.UserId,
                    test.Organisation.Id,
                    invoice.Id,
                    new DateOnly(2026, 8, 10)));

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());

        var after =
            await test.Db.SalesInvoices
                .AsNoTracking()
                .SingleAsync(x => x.Id == invoice.Id);

        Assert.Equal(25m, after.AmountPaid);
        Assert.NotEqual(
            InvoiceStatus.Voided,
            after.Status);
    }

    [Fact]
    public async Task VoidAsync_RejectsInvoiceWithCredit_EvenIfStatusIsPosted()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice =
            await test.SalesInvoices.CreateAndPostAsync(
                test.UserId,
                new SalesInvoiceRequest(
                    OrganisationId: test.Organisation.Id,
                    CustomerId: test.Customer.Id,
                    IssueDate: new DateOnly(2026, 8, 5),
                    DueDate: new DateOnly(2026, 9, 5),
                    Lines:
                    [
                        new SalesInvoiceLineRequest(
                            Description: "Credit void integrity test",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id)
                    ]));

        var credits =
            new SalesCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        await credits.CreateAsync(
            test.UserId,
            new SalesCreditNoteRequest(
                OrganisationId: test.Organisation.Id,
                SalesInvoiceId: invoice.Id,
                Date: new DateOnly(2026, 8, 8),
                Reason: "Credit void integrity test",
                Amount: 25m,
                RestockTrackedItems: false));

        var settledInvoice =
            await test.Db.SalesInvoices
                .SingleAsync(x => x.Id == invoice.Id);

        Assert.Equal(25m, settledInvoice.AmountCredited);
        Assert.Equal(
            InvoiceStatus.PartPaid,
            settledInvoice.Status);

        // Simulate inconsistent persisted status. AmountCredited remains
        // authoritative evidence that the invoice has settlement history.
        settledInvoice.Status = InvoiceStatus.Posted;
        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                test.SalesInvoices.VoidAsync(
                    test.UserId,
                    test.Organisation.Id,
                    invoice.Id,
                    new DateOnly(2026, 8, 10)));

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());

        var after =
            await test.Db.SalesInvoices
                .AsNoTracking()
                .SingleAsync(x => x.Id == invoice.Id);

        Assert.Equal(25m, after.AmountCredited);
        Assert.NotEqual(
            InvoiceStatus.Voided,
            after.Status);
    }
}
