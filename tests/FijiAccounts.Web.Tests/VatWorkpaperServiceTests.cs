using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class VatWorkpaperServiceTests
{
    [Fact]
    public async Task GetAsync_SummarisesPostedSalesAndPurchasesByVatTreatment()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var from = new DateOnly(2026, 8, 1);
        var to = new DateOnly(2026, 8, 31);

        await test.SalesInvoices.CreateAndPostAsync(
            test.UserId,
            new SalesInvoiceRequest(
                test.Organisation.Id,
                test.Customer.Id,
                new DateOnly(2026, 8, 15),
                new DateOnly(2026, 9, 14),
                [
                    SalesLine(test, 100m, VatTreatment.Standard),
                    SalesLine(test, 50m, VatTreatment.ZeroRated),
                    SalesLine(test, 25m, VatTreatment.Exempt),
                    SalesLine(test, 10m, VatTreatment.OutOfScope)
                ]));

        await test.SalesInvoices.CreateDraftAsync(
            test.UserId,
            new SalesInvoiceRequest(
                test.Organisation.Id,
                test.Customer.Id,
                new DateOnly(2026, 8, 20),
                new DateOnly(2026, 9, 19),
                [
                    SalesLine(test, 999m, VatTreatment.Standard)
                ]));

        await test.Purchasing.PostBillAsync(
            test.UserId,
            new SupplierBillRequest(
                test.Organisation.Id,
                test.Supplier.Id,
                "VAT-WORKPAPER-001",
                new DateOnly(2026, 8, 16),
                new DateOnly(2026, 9, 15),
                [
                    PurchaseLine(test, 80m, VatTreatment.Standard),
                    PurchaseLine(test, 40m, VatTreatment.ZeroRated),
                    PurchaseLine(test, 20m, VatTreatment.Exempt),
                    PurchaseLine(test, 8m, VatTreatment.OutOfScope)
                ]));

        var workpaper =
            await new VatWorkpaperService(test.Db)
                .GetAsync(
                    test.Organisation.Id,
                    from,
                    to);

        Assert.Equal(from, workpaper.From);
        Assert.Equal(to, workpaper.To);

        Assert.Equal(100m, workpaper.Sales.StandardNet);
        Assert.Equal(12.50m, workpaper.Sales.StandardTax);
        Assert.Equal(50m, workpaper.Sales.ZeroRatedNet);
        Assert.Equal(25m, workpaper.Sales.ExemptNet);
        Assert.Equal(10m, workpaper.Sales.OutOfScopeNet);

        Assert.Equal(80m, workpaper.Purchases.StandardNet);
        Assert.Equal(10m, workpaper.Purchases.StandardTax);
        Assert.Equal(40m, workpaper.Purchases.ZeroRatedNet);
        Assert.Equal(20m, workpaper.Purchases.ExemptNet);
        Assert.Equal(8m, workpaper.Purchases.OutOfScopeNet);

        Assert.Equal(12.50m, workpaper.OutputTax);
        Assert.Equal(10m, workpaper.InputTax);
        Assert.Equal(2.50m, workpaper.NetTax);
    }

    [Fact]
    public async Task GetAsync_WhenPeriodIsInvalid_RejectsRequest()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var exception =
            await Assert.ThrowsAsync<ArgumentException>(() =>
                new VatWorkpaperService(test.Db)
                    .GetAsync(
                        test.Organisation.Id,
                        new DateOnly(2026, 9, 1),
                        new DateOnly(2026, 8, 31)));

        Assert.Contains(
            "start date",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAsync_NetsCreditNotesAndSamePeriodReversals()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var date = new DateOnly(2026, 8, 15);

        var invoice =
            await test.SalesInvoices.CreateAndPostAsync(
                test.UserId,
                new SalesInvoiceRequest(
                    test.Organisation.Id,
                    test.Customer.Id,
                    date,
                    date.AddDays(30),
                    [
                        SalesLine(test, 100m, VatTreatment.Standard)
                    ]));

        var bill =
            await test.Purchasing.PostBillAsync(
                test.UserId,
                new SupplierBillRequest(
                    test.Organisation.Id,
                    test.Supplier.Id,
                    "VAT-CREDIT-WORKPAPER",
                    date,
                    date.AddDays(30),
                    [
                        PurchaseLine(test, 40m, VatTreatment.Standard)
                    ]));

        var salesCredits =
            new SalesCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var supplierCredits =
            new SupplierCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var salesCredit =
            await salesCredits.CreateAsync(
                test.UserId,
                new SalesCreditNoteRequest(
                    test.Organisation.Id,
                    invoice.Id,
                    date,
                    "Partial sales credit",
                    56.25m,
                    false));

        var supplierCredit =
            await supplierCredits.CreateAsync(
                test.UserId,
                new SupplierCreditNoteRequest(
                    test.Organisation.Id,
                    bill.Id,
                    date,
                    "Partial supplier credit",
                    22.50m,
                    false));

        var beforeReversal =
            await new VatWorkpaperService(test.Db)
                .GetAsync(
                    test.Organisation.Id,
                    new DateOnly(2026, 8, 1),
                    new DateOnly(2026, 8, 31));

        Assert.Equal(50m, beforeReversal.SalesCredits.Net);
        Assert.Equal(6.25m, beforeReversal.SalesCredits.Tax);
        Assert.Equal(20m, beforeReversal.SupplierCredits.Net);
        Assert.Equal(2.50m, beforeReversal.SupplierCredits.Tax);
        Assert.Equal(3.75m, beforeReversal.NetTax);

        await salesCredits.ReverseAsync(
            test.UserId,
            test.Organisation.Id,
            salesCredit.Id,
            date.AddDays(1),
            "Entered in error");

        await supplierCredits.ReverseAsync(
            test.UserId,
            test.Organisation.Id,
            supplierCredit.Id,
            date.AddDays(1),
            "Entered in error");

        var afterReversal =
            await new VatWorkpaperService(test.Db)
                .GetAsync(
                    test.Organisation.Id,
                    new DateOnly(2026, 8, 1),
                    new DateOnly(2026, 8, 31));

        Assert.Equal(0m, afterReversal.SalesCredits.Net);
        Assert.Equal(0m, afterReversal.SalesCredits.Tax);
        Assert.Equal(0m, afterReversal.SupplierCredits.Net);
        Assert.Equal(0m, afterReversal.SupplierCredits.Tax);
        Assert.Equal(7.50m, afterReversal.NetTax);
    }

    private static SalesInvoiceLineRequest SalesLine(
        AccountingTestDatabase test,
        decimal amount,
        VatTreatment treatment) =>
        new(
            treatment.ToString(),
            1m,
            amount,
            treatment,
            test.Account("4000").Id);

    private static SupplierBillLineRequest PurchaseLine(
        AccountingTestDatabase test,
        decimal amount,
        VatTreatment treatment) =>
        new(
            treatment.ToString(),
            1m,
            amount,
            treatment,
            test.Account("6500").Id);
}
