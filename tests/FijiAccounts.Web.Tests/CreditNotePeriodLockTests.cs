using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class CreditNotePeriodLockTests
{
    [Fact]
    public async Task SalesCreditNote_InsideLockedPeriod_IsRejected()
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
                            Description: "Locked period credit test",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id)
                    ]));

        await LockAugustAsync(test);

        var service =
            new SalesCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.CreateAsync(
                    test.UserId,
                    new SalesCreditNoteRequest(
                        OrganisationId: test.Organisation.Id,
                        SalesInvoiceId: invoice.Id,
                        Date: new DateOnly(2026, 8, 20),
                        Reason: "Should be locked",
                        Amount: 56.25m,
                        RestockTrackedItems: false)));

        Assert.Contains(
            "locked",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SupplierCreditNote_InsideLockedPeriod_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill =
            await test.Purchasing.PostBillAsync(
                test.UserId,
                new SupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "LOCKED-SUP-CREDIT-001",
                    BillDate: new DateOnly(2026, 8, 5),
                    DueDate: new DateOnly(2026, 9, 5),
                    Lines:
                    [
                        new SupplierBillLineRequest(
                            Description: "Locked period supplier credit test",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            ExpenseAccountId: test.Account("6500").Id)
                    ]));

        await LockAugustAsync(test);

        var service =
            new SupplierCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.CreateAsync(
                    test.UserId,
                    new SupplierCreditNoteRequest(
                        OrganisationId: test.Organisation.Id,
                        SupplierBillId: bill.Id,
                        Date: new DateOnly(2026, 8, 20),
                        Reason: "Should be locked",
                        Amount: 56.25m,
                        ReturnTrackedItems: false)));

        Assert.Contains(
            "locked",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task LockAugustAsync(
    AccountingTestDatabase test)
{
    var service =
        new AccountingPeriodService(
            test.Db,
            test.Access);

    var period =
        await service.CreateAsync(
            test.UserId,
            new AccountingPeriodRequest(
                OrganisationId: test.Organisation.Id,
                Name: "August 2026",
                StartsOn: new DateOnly(2026, 8, 1),
                EndsOn: new DateOnly(2026, 8, 31)));

    await service.SetLockedAsync(
        test.UserId,
        test.Organisation.Id,
        period.Id,
        true,
        acknowledgeWarnings: true);
}
}