using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class VatReturnAccountingTests
{
    [Fact]
    public async Task StandardSalesAndPurchases_ProduceCorrectVatPosition()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var from = new DateOnly(2026, 8, 1);
        var to = new DateOnly(2026, 8, 31);

        // $100 net sale + $12.50 VAT.
        await test.SalesInvoices.CreateAndPostAsync(
            test.UserId,
            new SalesInvoiceRequest(
                OrganisationId: test.Organisation.Id,
                CustomerId: test.Customer.Id,
                IssueDate: new DateOnly(2026, 8, 18),
                DueDate: new DateOnly(2026, 9, 17),
                Lines:
                [
                    new SalesInvoiceLineRequest(
                        Description: "Consulting",
                        Quantity: 1m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        RevenueAccountId: test.Account("4000").Id)
                ]));

        // $40 net purchase + $5.00 VAT.
        await test.Purchasing.PostBillAsync(
            test.UserId,
            new SupplierBillRequest(
                OrganisationId: test.Organisation.Id,
                SupplierId: test.Supplier.Id,
                SupplierReference: "VAT-BILL-001",
                BillDate: new DateOnly(2026, 8, 18),
                DueDate: new DateOnly(2026, 9, 17),
                Lines:
                [
                    new SupplierBillLineRequest(
                        Description: "Office expense",
                        Quantity: 1m,
                        UnitPrice: 40m,
                        VatTreatment: VatTreatment.Standard,
                        ExpenseAccountId: test.Account("6000").Id)
                ]));

        /*
         * Reproduce the TaxCenter.razor queries.
         */
        var sales =
            await test.Db.SalesInvoiceLines
                .AsNoTracking()
                .Where(x =>
                    x.SalesInvoice.OrganisationId ==
                        test.Organisation.Id &&
                    x.SalesInvoice.IssueDate >= from &&
                    x.SalesInvoice.IssueDate <= to &&
                    x.SalesInvoice.Status != InvoiceStatus.Draft &&
                    x.SalesInvoice.Status != InvoiceStatus.Voided)
                .Select(x => new
                {
                    x.VatTreatment,
                    Net = x.NetAmount,
                    Tax = x.VatAmount
                })
                .ToListAsync();

        var purchases =
            await test.Db.SupplierBillLines
                .AsNoTracking()
                .Where(x =>
                    x.SupplierBill.OrganisationId ==
                        test.Organisation.Id &&
                    x.SupplierBill.BillDate >= from &&
                    x.SupplierBill.BillDate <= to &&
                    x.SupplierBill.Status != BillStatus.Voided)
                .Select(x => new
                {
                    x.VatTreatment,
                    Net = x.NetAmount,
                    Tax = x.VatAmount
                })
                .ToListAsync();

        var standardSalesNet =
            sales
                .Where(x =>
                    x.VatTreatment == VatTreatment.Standard)
                .Sum(x => x.Net);

        var standardSalesTax =
            sales
                .Where(x =>
                    x.VatTreatment == VatTreatment.Standard)
                .Sum(x => x.Tax);

        var standardPurchaseNet =
            purchases
                .Where(x =>
                    x.VatTreatment == VatTreatment.Standard)
                .Sum(x => x.Net);

        var standardPurchaseTax =
            purchases
                .Where(x =>
                    x.VatTreatment == VatTreatment.Standard)
                .Sum(x => x.Tax);

        var outputTax = sales.Sum(x => x.Tax);
        var inputTax = purchases.Sum(x => x.Tax);
        var netTax = outputTax - inputTax;

        Assert.Equal(100m, standardSalesNet);
        Assert.Equal(12.50m, standardSalesTax);

        Assert.Equal(40m, standardPurchaseNet);
        Assert.Equal(5m, standardPurchaseTax);

        Assert.Equal(12.50m, outputTax);
        Assert.Equal(5m, inputTax);

        Assert.Equal(7.50m, netTax);
    }
}