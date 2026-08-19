using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class CustomerStatementAccountingTests
{
    [Fact]
    public async Task InvoiceVoid_AffectsStatementOnlyFromVoidDate()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice =
            await test.SalesInvoices.CreateAndPostAsync(
                test.UserId,
                new SalesInvoiceRequest(
                    OrganisationId: test.Organisation.Id,
                    CustomerId: test.Customer.Id,
                    IssueDate: new DateOnly(2026, 6, 1),
                    DueDate: new DateOnly(2026, 6, 30),
                    Lines:
                    [
                        new SalesInvoiceLineRequest(
                            Description: "Statement void test",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id)
                    ]));

        await test.SalesInvoices.VoidAsync(
            test.UserId,
            test.Organisation.Id,
            invoice.Id,
            new DateOnly(2026, 8, 5));

        var beforeVoid =
            await StatementBalanceAsync(
                test,
                new DateOnly(2026, 7, 31));

        var afterVoid =
            await StatementBalanceAsync(
                test,
                new DateOnly(2026, 8, 31));

        Assert.Equal(invoice.Total, beforeVoid);
        Assert.Equal(0m, afterVoid);
    }

    [Fact]
    public async Task ReceiptReversal_AffectsStatementOnlyFromReversalDate()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice =
            await CreateInvoiceAsync(
                test,
                "Statement receipt reversal");

        var receipts =
            new CustomerReceiptService(
                test.Db,
                test.Access,
                test.Posting,
                test.Reconciliation);

        var receipt =
            await receipts.RecordAsync(
                test.UserId,
                new CustomerReceiptRequest(
                    OrganisationId: test.Organisation.Id,
                    SalesInvoiceId: invoice.Id,
                    Date: new DateOnly(2026, 7, 10),
                    Reference: "STMT-REC-001",
                    Amount: 50m,
                    BankAccountId: test.Account("1000").Id));

        await receipts.ReverseAsync(
            test.UserId,
            test.Organisation.Id,
            receipt.Id,
            new DateOnly(2026, 8, 5),
            "Statement reversal test");

        var beforeReversal =
            await StatementBalanceAsync(
                test,
                new DateOnly(2026, 7, 31));

        var afterReversal =
            await StatementBalanceAsync(
                test,
                new DateOnly(2026, 8, 31));

        Assert.Equal(invoice.Total - 50m, beforeReversal);
        Assert.Equal(invoice.Total, afterReversal);
    }

    [Fact]
    public async Task CreditNoteReversal_AffectsStatementOnlyFromReversalDate()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice =
            await CreateInvoiceAsync(
                test,
                "Statement credit reversal");

        var credits =
            new SalesCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var credit =
            await credits.CreateAsync(
                test.UserId,
                new SalesCreditNoteRequest(
                    OrganisationId: test.Organisation.Id,
                    SalesInvoiceId: invoice.Id,
                    Date: new DateOnly(2026, 7, 10),
                    Reason: "Statement credit test",
                    Amount: 50m,
                    RestockTrackedItems: false));

        await credits.ReverseAsync(
            test.UserId,
            test.Organisation.Id,
            credit.Id,
            new DateOnly(2026, 8, 5),
            "Statement credit reversal");

        var beforeReversal =
            await StatementBalanceAsync(
                test,
                new DateOnly(2026, 7, 31));

        var afterReversal =
            await StatementBalanceAsync(
                test,
                new DateOnly(2026, 8, 31));

        Assert.Equal(invoice.Total - 50m, beforeReversal);
        Assert.Equal(invoice.Total, afterReversal);
    }

    private static async Task<SalesInvoice> CreateInvoiceAsync(
        AccountingTestDatabase test,
        string description)
    {
        return await test.SalesInvoices.CreateAndPostAsync(
            test.UserId,
            new SalesInvoiceRequest(
                OrganisationId: test.Organisation.Id,
                CustomerId: test.Customer.Id,
                IssueDate: new DateOnly(2026, 6, 1),
                DueDate: new DateOnly(2026, 6, 30),
                Lines:
                [
                    new SalesInvoiceLineRequest(
                        Description: description,
                        Quantity: 1m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        RevenueAccountId: test.Account("4000").Id)
                ]));
    }

    private static async Task<decimal> StatementBalanceAsync(
        AccountingTestDatabase test,
        DateOnly to)
    {
        var invoices =
            await test.Db.SalesInvoices
                .AsNoTracking()
                .Where(x =>
                    x.OrganisationId == test.Organisation.Id &&
                    x.CustomerId == test.Customer.Id &&
                    x.Status != InvoiceStatus.Draft &&
                    x.IssueDate <= to)
                .Select(x => x.Total)
                .ToListAsync();

        var invoiceVoids =
            await test.Db.SalesInvoiceVoids
                .AsNoTracking()
                .Where(x =>
                    x.OrganisationId == test.Organisation.Id &&
                    x.SalesInvoice.CustomerId == test.Customer.Id &&
                    x.VoidDate <= to)
                .Select(x => x.SalesInvoice.Total)
                .ToListAsync();

        var receipts =
            await test.Db.CustomerReceipts
                .AsNoTracking()
                .Where(x =>
                    x.OrganisationId == test.Organisation.Id &&
                    x.CustomerId == test.Customer.Id &&
                    x.ReceiptDate <= to)
                .Select(x => x.Amount)
                .ToListAsync();

        var receiptReversals =
            await test.Db.CustomerReceiptReversals
                .AsNoTracking()
                .Where(x =>
                    x.OrganisationId == test.Organisation.Id &&
                    x.CustomerReceipt.CustomerId == test.Customer.Id &&
                    x.ReversalDate <= to)
                .Select(x => x.CustomerReceipt.Amount)
                .ToListAsync();

        var credits =
            await test.Db.SalesCreditNotes
                .AsNoTracking()
                .Where(x =>
                    x.OrganisationId == test.Organisation.Id &&
                    x.SalesInvoice.CustomerId == test.Customer.Id &&
                    x.CreditDate <= to)
                .Select(x => x.Total)
                .ToListAsync();

        var creditReversals =
            await test.Db.SalesCreditNoteReversals
                .AsNoTracking()
                .Where(x =>
                    x.OrganisationId == test.Organisation.Id &&
                    x.SalesCreditNote.SalesInvoice.CustomerId ==
                        test.Customer.Id &&
                    x.ReversalDate <= to)
                .Select(x => x.SalesCreditNote.Total)
                .ToListAsync();

        return
            invoices.Sum() -
            invoiceVoids.Sum() -
            receipts.Sum() +
            receiptReversals.Sum() -
            credits.Sum() +
            creditReversals.Sum();
    }
}