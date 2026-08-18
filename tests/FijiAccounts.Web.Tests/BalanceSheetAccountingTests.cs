using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class BalanceSheetAccountingTests
{
    [Fact]
    public async Task PostedTransactions_PreserveBalanceSheetEquation()
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
                SupplierReference: "BS-BILL-001",
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
                new DateOnly(2026, 8, 31));

        var assets =
            report.Balances
                .Where(x =>
                    x.Type == AccountType.Asset)
                .Sum(x => x.DisplayAmount);

        var liabilities =
            report.Balances
                .Where(x =>
                    x.Type == AccountType.Liability)
                .Sum(x => x.DisplayAmount);

        var equity =
            report.Balances
                .Where(x =>
                    x.Type == AccountType.Equity)
                .Sum(x => x.DisplayAmount);

        var revenue =
            report.Balances
                .Where(x =>
                    x.Type == AccountType.Revenue)
                .Sum(x => x.DisplayAmount);

        var expenses =
            report.Balances
                .Where(x =>
                    x.Type == AccountType.Expense)
                .Sum(x => x.DisplayAmount);

        var currentProfit =
            revenue - expenses;

        Assert.Equal(
            assets,
            liabilities + equity + currentProfit);

        Assert.Equal(
            60m,
            currentProfit);
    }
}