using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class TrialBalanceAccountingTests
{
    [Fact]
    public async Task PostedTransactions_ProduceBalancedTrialBalance()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

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
                        RevenueAccountId:
                            test.Account("4000").Id)
                ]));

        await test.Purchasing.PostBillAsync(
            test.UserId,
            new SupplierBillRequest(
                OrganisationId: test.Organisation.Id,
                SupplierId: test.Supplier.Id,
                SupplierReference: "TB-BILL-001",
                BillDate: new DateOnly(2026, 8, 18),
                DueDate: new DateOnly(2026, 9, 17),
                Lines:
                [
                    new SupplierBillLineRequest(
                        Description: "Office expense",
                        Quantity: 1m,
                        UnitPrice: 40m,
                        VatTreatment: VatTreatment.Standard,
                        ExpenseAccountId:
                            test.Account("6000").Id)
                ]));

        var reports =
            new FinancialReportService(test.Db);

        var report =
            await reports.GetAsync(
                test.Organisation.Id,
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 18));

        var totalDebit =
            report.TrialBalance.Sum(x => x.Debit);

        var totalCredit =
            report.TrialBalance.Sum(x => x.Credit);

        Assert.Equal(
            totalDebit,
            totalCredit);

        Assert.Contains(
            report.TrialBalance,
            x => x.Code == "1100");

        Assert.Contains(
            report.TrialBalance,
            x => x.Code == "4000");

        Assert.Contains(
            report.TrialBalance,
            x => x.Code == "2000");

        Assert.Contains(
            report.TrialBalance,
            x => x.Code == "6000");

        Assert.Contains(
            report.TrialBalance,
            x => x.Code == "2100" ||
                 x.Code == "1200");
    }
}