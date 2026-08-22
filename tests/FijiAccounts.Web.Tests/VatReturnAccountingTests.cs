using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class VatReturnAccountingTests
{
    [Fact]
    public async Task StandardSalesAndPurchases_ProduceCorrectVatPosition()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        await test.SalesInvoices.CreateAndPostAsync(
            test.UserId,
            new SalesInvoiceRequest(
                test.Organisation.Id,
                test.Customer.Id,
                new DateOnly(2026, 8, 18),
                new DateOnly(2026, 9, 17),
                [
                    new SalesInvoiceLineRequest(
                        "Consulting",
                        1m,
                        100m,
                        VatTreatment.Standard,
                        test.Account("4000").Id)
                ]));

        await test.Purchasing.PostBillAsync(
            test.UserId,
            new SupplierBillRequest(
                test.Organisation.Id,
                test.Supplier.Id,
                "VAT-BILL-001",
                new DateOnly(2026, 8, 18),
                new DateOnly(2026, 9, 17),
                [
                    new SupplierBillLineRequest(
                        "Office expense",
                        1m,
                        40m,
                        VatTreatment.Standard,
                        test.Account("6000").Id)
                ]));

        var workpaper =
            await Workpaper(test).GetAsync(
                test.Organisation.Id,
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 31));

        Assert.Equal(100m, workpaper.Sales.StandardNet);
        Assert.Equal(12.50m, workpaper.OutputTax);
        Assert.Equal(40m, workpaper.Purchases.StandardNet);
        Assert.Equal(5m, workpaper.InputTax);
        Assert.Equal(7.50m, workpaper.NetTax);
    }

    [Fact]
    public async Task SalesInvoiceVoidedInLaterPeriod_RemainsInOriginalVatPeriod()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice =
            await test.SalesInvoices.CreateAndPostAsync(
                test.UserId,
                new SalesInvoiceRequest(
                    test.Organisation.Id,
                    test.Customer.Id,
                    new DateOnly(2026, 8, 31),
                    new DateOnly(2026, 9, 30),
                    [
                        new SalesInvoiceLineRequest(
                            "August taxable sale",
                            1m,
                            100m,
                            VatTreatment.Standard,
                            test.Account("4000").Id)
                    ]));

        await test.SalesInvoices.VoidAsync(
            test.UserId,
            test.Organisation.Id,
            invoice.Id,
            new DateOnly(2026, 9, 5));

        var august =
            await Workpaper(test).GetAsync(
                test.Organisation.Id,
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 31));

        var september =
            await Workpaper(test).GetAsync(
                test.Organisation.Id,
                new DateOnly(2026, 9, 1),
                new DateOnly(2026, 9, 30));

        Assert.Equal(100m, august.Sales.StandardNet);
        Assert.Equal(12.50m, august.OutputTax);
        Assert.Equal(-100m, september.Sales.StandardNet);
        Assert.Equal(-12.50m, september.OutputTax);
    }

    [Fact]
    public async Task SupplierBillVoidedInLaterPeriod_RemainsInOriginalVatPeriod()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill =
            await test.Purchasing.PostBillAsync(
                test.UserId,
                new SupplierBillRequest(
                    test.Organisation.Id,
                    test.Supplier.Id,
                    "VAT-VOID-BILL-001",
                    new DateOnly(2026, 8, 31),
                    new DateOnly(2026, 9, 30),
                    [
                        new SupplierBillLineRequest(
                            "August taxable purchase",
                            1m,
                            40m,
                            VatTreatment.Standard,
                            test.Account("6000").Id)
                    ]));

        await test.Purchasing.VoidBillAsync(
            test.UserId,
            test.Organisation.Id,
            bill.Id,
            new DateOnly(2026, 9, 5),
            "September correction");

        var august =
            await Workpaper(test).GetAsync(
                test.Organisation.Id,
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 31));

        var september =
            await Workpaper(test).GetAsync(
                test.Organisation.Id,
                new DateOnly(2026, 9, 1),
                new DateOnly(2026, 9, 30));

        Assert.Equal(40m, august.Purchases.StandardNet);
        Assert.Equal(5m, august.InputTax);
        Assert.Equal(-40m, september.Purchases.StandardNet);
        Assert.Equal(-5m, september.InputTax);
    }

    private static VatWorkpaperService Workpaper(
        AccountingTestDatabase test) =>
        new(test.Db);
}
