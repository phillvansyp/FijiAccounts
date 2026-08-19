using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class ProfitAndLossAccountingTests
{
    [Fact]
    public async Task PostedSalesAndExpenses_ProduceCorrectNetProfit()
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
                SupplierReference: "PL-BILL-001",
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

        var netProfit =
            revenue - expenses;

        Assert.Equal(100m, revenue);
        Assert.Equal(40m, expenses);
        Assert.Equal(60m, netProfit);
    }

        [Fact]
    public async Task InvoiceVoidedInLaterPeriod_ReversesProfitOnlyInVoidPeriod()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice =
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

        await test.SalesInvoices.VoidAsync(
            test.UserId,
            test.Organisation.Id,
            invoice.Id,
            new DateOnly(2026, 8, 5));

        var reports =
            new FinancialReportService(test.Db);

        var july =
            await reports.GetAsync(
                test.Organisation.Id,
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 31));

        var august =
            await reports.GetAsync(
                test.Organisation.Id,
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 31));

        var julyRevenue =
            july.Balances
                .Where(x =>
                    x.Type == AccountType.Revenue)
                .Sum(x => x.DisplayAmount);

        var augustRevenue =
            august.Balances
                .Where(x =>
                    x.Type == AccountType.Revenue)
                .Sum(x => x.DisplayAmount);

        Assert.Equal(100m, julyRevenue);
        Assert.Equal(-100m, augustRevenue);
    }

        [Fact]
    public async Task SupplierBillVoidedInLaterPeriod_ReversesExpenseOnlyInVoidPeriod()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill =
            await test.Purchasing.PostBillAsync(
                test.UserId,
                new SupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "PL-VOID-BILL-001",
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

        await test.Purchasing.VoidBillAsync(
            test.UserId,
            test.Organisation.Id,
            bill.Id,
            new DateOnly(2026, 8, 5),
            "August correction");

        var reports =
            new FinancialReportService(test.Db);

        var july =
            await reports.GetAsync(
                test.Organisation.Id,
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 31));

        var august =
            await reports.GetAsync(
                test.Organisation.Id,
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 31));

        var julyExpenses =
            july.Balances
                .Where(x =>
                    x.Type == AccountType.Expense)
                .Sum(x => x.DisplayAmount);

        var augustExpenses =
            august.Balances
                .Where(x =>
                    x.Type == AccountType.Expense)
                .Sum(x => x.DisplayAmount);

        Assert.Equal(40m, julyExpenses);
        Assert.Equal(-40m, augustExpenses);
    }
}