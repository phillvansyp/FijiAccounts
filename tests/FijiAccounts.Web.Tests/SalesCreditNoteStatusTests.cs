using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SalesCreditNoteStatusTests
{
    [Fact]
    public async Task PartialCredit_ChangesPostedInvoiceToPartPaid()
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
                            Description: "Partial credit status test",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id)
                    ]));

        Assert.Equal(
            InvoiceStatus.Posted,
            invoice.Status);

        var service =
            new SalesCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        await service.CreateAsync(
            test.UserId,
            new SalesCreditNoteRequest(
                OrganisationId: test.Organisation.Id,
                SalesInvoiceId: invoice.Id,
                Date: new DateOnly(2026, 8, 10),
                Reason: "Partial credit",
                Amount: 56.25m,
                RestockTrackedItems: false));

        var reloaded =
            await test.Db.SalesInvoices
                .AsNoTracking()
                .SingleAsync(x => x.Id == invoice.Id);

        Assert.Equal(
            56.25m,
            reloaded.AmountCredited);

        Assert.Equal(
            InvoiceStatus.PartPaid,
            reloaded.Status);
    }

    [Fact]
public async Task FullCredit_ChangesInvoiceToCredited()
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
                        Description: "Full credit status test",
                        Quantity: 1m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        RevenueAccountId: test.Account("4000").Id)
                ]));

    var service =
        new SalesCreditNoteService(
            test.Db,
            test.Access,
            test.Posting);

    await service.CreateAsync(
        test.UserId,
        new SalesCreditNoteRequest(
            OrganisationId: test.Organisation.Id,
            SalesInvoiceId: invoice.Id,
            Date: new DateOnly(2026, 8, 10),
            Reason: "Full credit",
            Amount: 112.50m,
            RestockTrackedItems: false));

    var reloaded =
        await test.Db.SalesInvoices
            .AsNoTracking()
            .SingleAsync(x => x.Id == invoice.Id);

    Assert.Equal(112.50m, reloaded.AmountCredited);
    Assert.Equal(InvoiceStatus.Credited, reloaded.Status);
}

[Fact]
public async Task PartialCredit_OnPartPaidInvoice_RemainsPartPaid()
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
                        Description: "Part paid credit status test",
                        Quantity: 1m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        RevenueAccountId: test.Account("4000").Id)
                ]));

    var bank = test.Account("1000");

    await new CustomerReceiptService(
            test.Db,
            test.Access,
            test.Posting,
            test.Reconciliation)
        .RecordAsync(
            test.UserId,
            new CustomerReceiptRequest(
                OrganisationId: test.Organisation.Id,
                SalesInvoiceId: invoice.Id,
                Date: new DateOnly(2026, 8, 8),
                Reference: "RCPT-CREDIT-001",
                Amount: 25m,
                BankAccountId: bank.Id));

    var service =
        new SalesCreditNoteService(
            test.Db,
            test.Access,
            test.Posting);

    await service.CreateAsync(
        test.UserId,
        new SalesCreditNoteRequest(
            OrganisationId: test.Organisation.Id,
            SalesInvoiceId: invoice.Id,
            Date: new DateOnly(2026, 8, 10),
            Reason: "Partial credit after payment",
            Amount: 25m,
            RestockTrackedItems: false));

    var reloaded =
        await test.Db.SalesInvoices
            .AsNoTracking()
            .SingleAsync(x => x.Id == invoice.Id);

    Assert.Equal(25m, reloaded.AmountPaid);
    Assert.Equal(25m, reloaded.AmountCredited);
    Assert.Equal(InvoiceStatus.PartPaid, reloaded.Status);
}
}