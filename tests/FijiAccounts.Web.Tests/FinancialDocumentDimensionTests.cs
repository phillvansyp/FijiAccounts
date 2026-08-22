using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class FinancialDocumentDimensionTests
{
    [Fact]
    public async Task SalesInvoiceAndReceipt_PreserveSelectedDimension()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var dimension = await AddRetailDimensionAsync(test);
        var invoice = await test.SalesInvoices.CreateAndPostAsync(
            test.UserId,
            new SalesInvoiceRequest(
                test.Organisation.Id,
                test.Customer.Id,
                new DateOnly(2026, 8, 23),
                new DateOnly(2026, 9, 22),
                [new("Dimension sale", 1m, 100m, VatTreatment.Standard, test.Account("4000").Id)],
                dimension.BranchId,
                dimension.DivisionId));
        var receipts = new CustomerReceiptService(
            test.Db,
            test.Access,
            test.Posting,
            test.Reconciliation,
            test.Notifications);
        var receipt = await receipts.RecordAsync(
            test.UserId,
            new CustomerReceiptRequest(
                test.Organisation.Id,
                invoice.Id,
                new DateOnly(2026, 8, 24),
                "DIM-RECEIPT",
                invoice.Total,
                test.Account("1000").Id));

        Assert.Equal(dimension.BranchId, invoice.BranchId);
        Assert.Equal(dimension.DivisionId, invoice.DivisionId);
        Assert.Equal(dimension.BranchId, receipt.BranchId);
        Assert.Equal(dimension.DivisionId, receipt.DivisionId);
        await AssertJournalDimensionAsync(test, invoice.PostedJournalId!.Value, dimension);
        await AssertJournalDimensionAsync(test, receipt.PostedJournalId, dimension);
    }

    [Fact]
    public async Task SupplierBillAndPayment_PreserveSelectedDimension()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var dimension = await AddRetailDimensionAsync(test);
        var bill = await test.Purchasing.PostBillAsync(
            test.UserId,
            new SupplierBillRequest(
                test.Organisation.Id,
                test.Supplier.Id,
                "DIM-BILL",
                new DateOnly(2026, 8, 23),
                new DateOnly(2026, 9, 22),
                [new("Dimension expense", 1m, 100m, VatTreatment.Standard, test.Account("6000").Id)],
                dimension.BranchId,
                dimension.DivisionId));
        var payment = await test.Purchasing.PayBillAsync(
            test.UserId,
            new SupplierPaymentRequest(
                test.Organisation.Id,
                bill.Id,
                new DateOnly(2026, 8, 24),
                "DIM-PAYMENT",
                bill.Total,
                test.Account("1000").Id));

        Assert.Equal(dimension.BranchId, bill.BranchId);
        Assert.Equal(dimension.DivisionId, bill.DivisionId);
        Assert.Equal(dimension.BranchId, payment.BranchId);
        Assert.Equal(dimension.DivisionId, payment.DivisionId);
        await AssertJournalDimensionAsync(test, bill.PostedJournalId, dimension);
        await AssertJournalDimensionAsync(test, payment.PostedJournalId, dimension);
    }

    private static async Task<EnterprisePostingDimension> AddRetailDimensionAsync(
        AccountingTestDatabase test)
    {
        var structures = new EnterpriseStructureService(test.Db);
        var branch = await structures.AddBranchAsync(
            test.Organisation.Id,
            "NADI",
            "Nadi Branch");
        var division = await structures.AddDivisionAsync(
            test.Organisation.Id,
            branch.Id,
            "RETAIL",
            "Retail");
        return new EnterprisePostingDimension(branch.Id, division.Id);
    }

    private static async Task AssertJournalDimensionAsync(
        AccountingTestDatabase test,
        Guid journalId,
        EnterprisePostingDimension dimension)
    {
        var lines = await test.Db.PostedJournalLines
            .AsNoTracking()
            .Where(x => x.PostedJournalId == journalId)
            .ToListAsync();

        Assert.NotEmpty(lines);
        Assert.All(lines, line =>
        {
            Assert.Equal(dimension.BranchId, line.BranchId);
            Assert.Equal(dimension.DivisionId, line.DivisionId);
        });
    }
}
