using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class AgedReceivablesAccountingTests
{
    [Fact]
    public async Task ReceivableAsAtDate_UsesOnlyReceiptsDatedOnOrBeforeReportDate()
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
                            Description: "Consulting",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id)
                    ]));

        var asAt =
            new DateOnly(2026, 7, 31);

        var invoices =
            await test.Db.SalesInvoices
                .AsNoTracking()
                .Where(x =>
                    x.OrganisationId == test.Organisation.Id &&
                    x.IssueDate <= asAt &&
                    x.Status != InvoiceStatus.Draft &&
                    x.Status != InvoiceStatus.Voided)
                .ToListAsync();

        var invoiceIds =
            invoices.Select(x => x.Id).ToArray();

        var paid =
            await test.Db.CustomerReceiptAllocations
                .AsNoTracking()
                .Where(x =>
                    invoiceIds.Contains(x.SalesInvoiceId) &&
                    x.CustomerReceipt.ReceiptDate <= asAt)
                .GroupBy(x => x.SalesInvoiceId)
                .Select(x => new
                {
                    x.Key,
                    Amount = x.Sum(y => y.Amount)
                })
                .ToDictionaryAsync(
                    x => x.Key,
                    x => x.Amount);

        var credited =
            await test.Db.SalesCreditNotes
                .AsNoTracking()
                .Where(x =>
                    invoiceIds.Contains(x.SalesInvoiceId) &&
                    x.CreditDate <= asAt)
                .GroupBy(x => x.SalesInvoiceId)
                .Select(x => new
                {
                    x.Key,
                    Amount = x.Sum(y => y.Total)
                })
                .ToDictionaryAsync(
                    x => x.Key,
                    x => x.Amount);

        var outstanding =
            invoices.Sum(x =>
                Math.Max(
                    0,
                    x.Total -
                    paid.GetValueOrDefault(x.Id) -
                    credited.GetValueOrDefault(x.Id)));

        Assert.Equal(invoice.Total, outstanding);

        var daysOverdue =
            asAt.DayNumber -
            invoice.DueDate.DayNumber;

        Assert.InRange(daysOverdue, 31, 60);
    }

    [Fact]
public async Task ReceivableAsAtDate_IncludesInvoiceUntilItsVoidDate()
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
                        Description: "Historical ageing sale",
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

    var asAtBeforeVoid =
        new DateOnly(2026, 7, 31);

    var voidedBeforeAsAt =
        await test.Db.SalesInvoiceVoids
            .AsNoTracking()
            .AnyAsync(x =>
                x.SalesInvoiceId == invoice.Id &&
                x.VoidDate <= asAtBeforeVoid);

    Assert.False(voidedBeforeAsAt);

    var asAtAfterVoid =
        new DateOnly(2026, 8, 31);

    var voidedAfterAsAt =
        await test.Db.SalesInvoiceVoids
            .AsNoTracking()
            .AnyAsync(x =>
                x.SalesInvoiceId == invoice.Id &&
                x.VoidDate <= asAtAfterVoid);

    Assert.True(voidedAfterAsAt);
}

[Fact]
public async Task ReceivableAsAtDate_CreditReversalOnlyRestoresBalanceFromReversalDate()
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
                        Description: "Ageing credit reversal sale",
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

    var credit =
        await credits.CreateAsync(
            test.UserId,
            new SalesCreditNoteRequest(
                OrganisationId: test.Organisation.Id,
                SalesInvoiceId: invoice.Id,
                Date: new DateOnly(2026, 7, 10),
                Reason: "Temporary credit",
                Amount: 56.25m,
                RestockTrackedItems: false));

    await credits.ReverseAsync(
        test.UserId,
        test.Organisation.Id,
        credit.Id,
        new DateOnly(2026, 8, 5),
        "Reverse temporary credit");

    var julyAsAt =
        new DateOnly(2026, 7, 31);

    var julyReversed =
        await test.Db.SalesCreditNoteReversals
            .AsNoTracking()
            .AnyAsync(x =>
                x.SalesCreditNoteId == credit.Id &&
                x.ReversalDate <= julyAsAt);

    Assert.False(julyReversed);

    var augustAsAt =
        new DateOnly(2026, 8, 31);

    var augustReversed =
        await test.Db.SalesCreditNoteReversals
            .AsNoTracking()
            .AnyAsync(x =>
                x.SalesCreditNoteId == credit.Id &&
                x.ReversalDate <= augustAsAt);

    Assert.True(augustReversed);
}

    [Fact]
public async Task ReceivableAsAtDate_ReceiptReversalRestoresBalanceOnlyFromReversalDate()
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
                        Description: "Receipt reversal ageing sale",
                        Quantity: 1m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        RevenueAccountId: test.Account("4000").Id)
                ]));

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
            Reference: "AGING-RECEIPT-001",
            Amount: invoice.Total,
            BankAccountId: test.Account("1000").Id));

await receipts.ReverseAsync(
    test.UserId,
    test.Organisation.Id,
    receipt.Id,
    new DateOnly(2026, 8, 5),
    "Reverse receipt for ageing test");

    var julyAsAt = new DateOnly(2026, 7, 31);

    var julyReversed =
        await test.Db.CustomerReceiptReversals
            .AsNoTracking()
            .AnyAsync(x =>
                x.CustomerReceiptId == receipt.Id &&
                x.ReversalDate <= julyAsAt);

    Assert.False(julyReversed);

    var augustAsAt = new DateOnly(2026, 8, 31);

    var augustReversed =
        await test.Db.CustomerReceiptReversals
            .AsNoTracking()
            .AnyAsync(x =>
                x.CustomerReceiptId == receipt.Id &&
                x.ReversalDate <= augustAsAt);

    Assert.True(augustReversed);
}
}