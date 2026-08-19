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
    liabilities + equity);

        Assert.Equal(
            60m,
            currentProfit);

        Assert.Contains(
    report.Balances,
    x =>
        x.Type == AccountType.Equity &&
        x.Name == "Accumulated earnings" &&
        x.DisplayAmount == 60m);
    }

    [Fact]
public async Task BalanceSheet_AsAtDate_IsIndependentOfProfitAndLossFromDate()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    await test.SalesInvoices.CreateAndPostAsync(
        test.UserId,
        new SalesInvoiceRequest(
            OrganisationId: test.Organisation.Id,
            CustomerId: test.Customer.Id,
            IssueDate: new DateOnly(2026, 7, 15),
            DueDate: new DateOnly(2026, 8, 14),
            Lines:
            [
                new SalesInvoiceLineRequest(
                    Description: "July consulting",
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
            SupplierReference: "BS-JULY-001",
            BillDate: new DateOnly(2026, 7, 15),
            DueDate: new DateOnly(2026, 8, 14),
            Lines:
            [
                new SupplierBillLineRequest(
                    Description: "July office expense",
                    Quantity: 1m,
                    UnitPrice: 40m,
                    VatTreatment: VatTreatment.Standard,
                    ExpenseAccountId:
                        test.Account("6000").Id)
            ]));

    var reports =
        new FinancialReportService(test.Db);

    var yearToDate =
        await reports.GetAsync(
            test.Organisation.Id,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 8, 31));

    var augustOnly =
        await reports.GetAsync(
            test.Organisation.Id,
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31));

    var yearToDateAssets =
        yearToDate.Balances
            .Where(x => x.Type == AccountType.Asset)
            .Sum(x => x.DisplayAmount);

    var augustAssets =
        augustOnly.Balances
            .Where(x => x.Type == AccountType.Asset)
            .Sum(x => x.DisplayAmount);

    var yearToDateLiabilities =
        yearToDate.Balances
            .Where(x => x.Type == AccountType.Liability)
            .Sum(x => x.DisplayAmount);

    var augustLiabilities =
        augustOnly.Balances
            .Where(x => x.Type == AccountType.Liability)
            .Sum(x => x.DisplayAmount);

    var yearToDateEquity =
        yearToDate.Balances
            .Where(x => x.Type == AccountType.Equity)
            .Sum(x => x.DisplayAmount);

    var augustEquity =
        augustOnly.Balances
            .Where(x => x.Type == AccountType.Equity)
            .Sum(x => x.DisplayAmount);

    var yearToDateProfit =
        yearToDate.Balances
            .Where(x => x.Type == AccountType.Revenue)
            .Sum(x => x.DisplayAmount) -
        yearToDate.Balances
            .Where(x => x.Type == AccountType.Expense)
            .Sum(x => x.DisplayAmount);

    var augustProfit =
        augustOnly.Balances
            .Where(x => x.Type == AccountType.Revenue)
            .Sum(x => x.DisplayAmount) -
        augustOnly.Balances
            .Where(x => x.Type == AccountType.Expense)
            .Sum(x => x.DisplayAmount);

    Assert.Equal(yearToDateAssets, augustAssets);
    Assert.Equal(yearToDateLiabilities, augustLiabilities);

    Assert.Equal(60m, yearToDateProfit);
    Assert.Equal(0m, augustProfit);

    Assert.Equal(
    yearToDateEquity,
    augustEquity);

Assert.Equal(
    yearToDateAssets,
    yearToDateLiabilities +
    yearToDateEquity);

Assert.Equal(
    augustAssets,
    augustLiabilities +
    augustEquity);
}
}