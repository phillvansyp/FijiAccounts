using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class LiveFinancialUpdateTests
{
    [Fact]
    public async Task PartialCustomerReceipt_PublishesOrganisationUpdate()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var today =
            DateOnly.FromDateTime(DateTime.Today);

        var invoice =
            await test.SalesInvoices.CreateAndPostAsync(
                test.UserId,
                new SalesInvoiceRequest(
                    test.Organisation.Id,
                    test.Customer.Id,
                    today,
                    today.AddDays(14),
                    [
                        new SalesInvoiceLineRequest(
                            "Live update test",
                            1m,
                            100m,
                            VatTreatment.Exempt,
                            test.Account("4000").Id)
                    ]));

        var updates = new List<Guid>();
        using var subscription =
            test.Updates.Subscribe(updates.Add);
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
                test.Organisation.Id,
                invoice.Id,
                today,
                "LIVE-RECEIPT",
                25m,
                test.Account("1000").Id));

        Assert.Equal([test.Organisation.Id], updates);
    }
}
