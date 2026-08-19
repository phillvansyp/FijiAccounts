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

        [Fact]
    public async Task SalesInvoiceVoidedInLaterPeriod_RemainsInOriginalVatPeriod()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice =
            await test.SalesInvoices.CreateAndPostAsync(
                test.UserId,
                new SalesInvoiceRequest(
                    OrganisationId: test.Organisation.Id,
                    CustomerId: test.Customer.Id,
                    IssueDate: new DateOnly(2026, 8, 31),
                    DueDate: new DateOnly(2026, 9, 30),
                    Lines:
                    [
                        new SalesInvoiceLineRequest(
                            Description: "August taxable sale",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id)
                    ]));

        await test.SalesInvoices.VoidAsync(
            test.UserId,
            test.Organisation.Id,
            invoice.Id,
            new DateOnly(2026, 9, 5));

        var augustFrom = new DateOnly(2026, 8, 1);
        var augustTo = new DateOnly(2026, 8, 31);

        var augustSales =
    await test.Db.SalesInvoiceLines
        .AsNoTracking()
        .Where(x =>
            x.SalesInvoice.OrganisationId ==
                test.Organisation.Id &&
            x.SalesInvoice.IssueDate >= augustFrom &&
            x.SalesInvoice.IssueDate <= augustTo &&
            x.SalesInvoice.Status != InvoiceStatus.Draft)
        .Select(x => new
        {
            x.NetAmount,
            x.VatAmount
        })
        .ToListAsync();

var augustSalesVoids =
    await test.Db.SalesInvoiceLines
        .AsNoTracking()
        .Where(x =>
            x.SalesInvoice.OrganisationId ==
                test.Organisation.Id &&
            test.Db.SalesInvoiceVoids.Any(v =>
                v.OrganisationId == test.Organisation.Id &&
                v.SalesInvoiceId == x.SalesInvoiceId &&
                v.VoidDate >= augustFrom &&
                v.VoidDate <= augustTo))
        .Select(x => new
        {
            NetAmount = -x.NetAmount,
            VatAmount = -x.VatAmount
        })
        .ToListAsync();

var septemberFrom = new DateOnly(2026, 9, 1);
var septemberTo = new DateOnly(2026, 9, 30);

var septemberSalesVoids =
    await test.Db.SalesInvoiceLines
        .AsNoTracking()
        .Where(x =>
            x.SalesInvoice.OrganisationId ==
                test.Organisation.Id &&
            test.Db.SalesInvoiceVoids.Any(v =>
                v.OrganisationId == test.Organisation.Id &&
                v.SalesInvoiceId == x.SalesInvoiceId &&
                v.VoidDate >= septemberFrom &&
                v.VoidDate <= septemberTo))
        .Select(x => new
        {
            NetAmount = -x.NetAmount,
            VatAmount = -x.VatAmount
        })
        .ToListAsync();

Assert.Equal(
    100m,
    augustSales.Sum(x => x.NetAmount) +
    augustSalesVoids.Sum(x => x.NetAmount));

Assert.Equal(
    12.50m,
    augustSales.Sum(x => x.VatAmount) +
    augustSalesVoids.Sum(x => x.VatAmount));

Assert.Equal(
    -100m,
    septemberSalesVoids.Sum(x => x.NetAmount));

Assert.Equal(
    -12.50m,
    septemberSalesVoids.Sum(x => x.VatAmount));
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
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "VAT-VOID-BILL-001",
                    BillDate: new DateOnly(2026, 8, 31),
                    DueDate: new DateOnly(2026, 9, 30),
                    Lines:
                    [
                        new SupplierBillLineRequest(
                            Description: "August taxable purchase",
                            Quantity: 1m,
                            UnitPrice: 40m,
                            VatTreatment: VatTreatment.Standard,
                            ExpenseAccountId: test.Account("6000").Id)
                    ]));

        await test.Purchasing.VoidBillAsync(
            test.UserId,
            test.Organisation.Id,
            bill.Id,
            new DateOnly(2026, 9, 5),
            "September correction");

        var augustFrom = new DateOnly(2026, 8, 1);
        var augustTo = new DateOnly(2026, 8, 31);

        var augustPurchases =
    await test.Db.SupplierBillLines
        .AsNoTracking()
        .Where(x =>
            x.SupplierBill.OrganisationId ==
                test.Organisation.Id &&
            x.SupplierBill.BillDate >= augustFrom &&
            x.SupplierBill.BillDate <= augustTo)
        .Select(x => new
        {
            x.NetAmount,
            x.VatAmount
        })
        .ToListAsync();

var augustPurchaseVoids =
    await test.Db.SupplierBillLines
        .AsNoTracking()
        .Where(x =>
            x.SupplierBill.OrganisationId ==
                test.Organisation.Id &&
            test.Db.SupplierBillVoids.Any(v =>
                v.OrganisationId == test.Organisation.Id &&
                v.SupplierBillId == x.SupplierBillId &&
                v.VoidDate >= augustFrom &&
                v.VoidDate <= augustTo))
        .Select(x => new
        {
            NetAmount = -x.NetAmount,
            VatAmount = -x.VatAmount
        })
        .ToListAsync();

var septemberFrom = new DateOnly(2026, 9, 1);
var septemberTo = new DateOnly(2026, 9, 30);

var septemberPurchaseVoids =
    await test.Db.SupplierBillLines
        .AsNoTracking()
        .Where(x =>
            x.SupplierBill.OrganisationId ==
                test.Organisation.Id &&
            test.Db.SupplierBillVoids.Any(v =>
                v.OrganisationId == test.Organisation.Id &&
                v.SupplierBillId == x.SupplierBillId &&
                v.VoidDate >= septemberFrom &&
                v.VoidDate <= septemberTo))
        .Select(x => new
        {
            NetAmount = -x.NetAmount,
            VatAmount = -x.VatAmount
        })
        .ToListAsync();

Assert.Equal(
    40m,
    augustPurchases.Sum(x => x.NetAmount) +
    augustPurchaseVoids.Sum(x => x.NetAmount));

Assert.Equal(
    5m,
    augustPurchases.Sum(x => x.VatAmount) +
    augustPurchaseVoids.Sum(x => x.VatAmount));

Assert.Equal(
    -40m,
    septemberPurchaseVoids.Sum(x => x.NetAmount));

Assert.Equal(
    -5m,
    septemberPurchaseVoids.Sum(x => x.VatAmount));
    }
}