using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class VatCreditNoteAccountingTests
{
    [Fact]
    public async Task SalesAndSupplierCredits_AdjustVatPositionCorrectly()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var from = new DateOnly(2026, 8, 1);
        var to = new DateOnly(2026, 8, 31);

        // ---------------------------------------------------------
        // SALE
        // $100.00 net + $12.50 VAT = $112.50
        // ---------------------------------------------------------

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
                            Description: "VAT credit test sale",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id)
                    ]));

        // Credit exactly half:
        // $50.00 net + $6.25 VAT = $56.25

        var salesCreditService =
            new SalesCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var salesCredit =
            await salesCreditService.CreateAsync(
                test.UserId,
                new SalesCreditNoteRequest(
                    OrganisationId: test.Organisation.Id,
                    SalesInvoiceId: invoice.Id,
                    Date: new DateOnly(2026, 8, 10),
                    Reason: "Half sale credited",
                    Amount: 56.25m,
                    RestockTrackedItems: false));

        // ---------------------------------------------------------
        // PURCHASE
        // $40.00 net + $5.00 VAT = $45.00
        // ---------------------------------------------------------

        var bill =
            await test.Purchasing.PostBillAsync(
                test.UserId,
                new SupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "VAT-CREDIT-BILL-001",
                    BillDate: new DateOnly(2026, 8, 5),
                    DueDate: new DateOnly(2026, 9, 5),
                    Lines:
                    [
                        new SupplierBillLineRequest(
                            Description: "VAT credit test purchase",
                            Quantity: 1m,
                            UnitPrice: 40m,
                            VatTreatment: VatTreatment.Standard,
                            ExpenseAccountId: test.Account("6000").Id)
                    ]));

        // Credit exactly half:
        // $20.00 net + $2.50 VAT = $22.50

        var supplierCreditService =
            new SupplierCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var supplierCredit =
            await supplierCreditService.CreateAsync(
                test.UserId,
                new SupplierCreditNoteRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierBillId: bill.Id,
                    Date: new DateOnly(2026, 8, 10),
                    Reason: "Half purchase credited",
                    Amount: 22.50m,
                    ReturnTrackedItems: false));

        // ---------------------------------------------------------
        // Verify credit-note calculations themselves.
        // ---------------------------------------------------------

        Assert.Equal(50m, salesCredit.Subtotal);
        Assert.Equal(6.25m, salesCredit.VatTotal);
        Assert.Equal(56.25m, salesCredit.Total);

        Assert.Equal(20m, supplierCredit.Subtotal);
        Assert.Equal(2.50m, supplierCredit.VatTotal);
        Assert.Equal(22.50m, supplierCredit.Total);

        // ---------------------------------------------------------
        // Reproduce TaxCenter.razor calculations.
        // ---------------------------------------------------------

        var salesTax =
            await test.Db.SalesInvoiceLines
                .AsNoTracking()
                .Where(x =>
                    x.SalesInvoice.OrganisationId ==
                        test.Organisation.Id &&
                    x.SalesInvoice.IssueDate >= from &&
                    x.SalesInvoice.IssueDate <= to &&
                    x.SalesInvoice.Status != InvoiceStatus.Draft &&
                    x.SalesInvoice.Status != InvoiceStatus.Voided)
                .SumAsync(x => x.VatAmount);

        var purchaseTax =
            await test.Db.SupplierBillLines
                .AsNoTracking()
                .Where(x =>
                    x.SupplierBill.OrganisationId ==
                        test.Organisation.Id &&
                    x.SupplierBill.BillDate >= from &&
                    x.SupplierBill.BillDate <= to &&
                    x.SupplierBill.Status != BillStatus.Voided)
                .SumAsync(x => x.VatAmount);

        var creditTax =
            await test.Db.SalesCreditNotes
                .AsNoTracking()
                .Where(x =>
                    x.OrganisationId == test.Organisation.Id &&
                    x.CreditDate >= from &&
                    x.CreditDate <= to)
                .SumAsync(x => x.VatTotal);

        var supplierCreditTax =
            await test.Db.SupplierCreditNotes
                .AsNoTracking()
                .Where(x =>
                    x.OrganisationId == test.Organisation.Id &&
                    x.CreditDate >= from &&
                    x.CreditDate <= to)
                .SumAsync(x => x.VatTotal);

        var outputTax =
            salesTax - creditTax;

        var inputTax =
            purchaseTax - supplierCreditTax;

        var netTax =
            outputTax - inputTax;

        // Original output VAT = $12.50
        Assert.Equal(12.50m, salesTax);

        // Less sales credit VAT = $6.25
        Assert.Equal(6.25m, creditTax);

        // Remaining output VAT = $6.25
        Assert.Equal(6.25m, outputTax);

        // Original input VAT = $5.00
        Assert.Equal(5m, purchaseTax);

        // Less supplier credit VAT = $2.50
        Assert.Equal(2.50m, supplierCreditTax);

        // Remaining input VAT = $2.50
        Assert.Equal(2.50m, inputTax);

        // $6.25 output - $2.50 input = $3.75 payable
        Assert.Equal(3.75m, netTax);
    }
}