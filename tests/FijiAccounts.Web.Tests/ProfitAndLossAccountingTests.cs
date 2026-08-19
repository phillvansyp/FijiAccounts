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


    [Fact]
    public async Task SalesCreditReversedInLaterPeriod_ReversesCreditOnlyInReversalPeriod()
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
                            Description: "July credit-note sale",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId:
                                test.Account("4000").Id)
                    ]));

        var credits =
            new SalesCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var credit =
            await credits.CreateAsync(
                test.UserId,
                new SalesCreditNoteRequest(
                    OrganisationId: test.Organisation.Id,
                    SalesInvoiceId: invoice.Id,
                    Date: new DateOnly(2026, 7, 20),
                    Reason: "July sales credit",
                    Amount: 50m,
                    RestockTrackedItems: false));

        await credits.ReverseAsync(
            test.UserId,
            test.Organisation.Id,
            credit.Id,
            new DateOnly(2026, 8, 5),
            "August reversal");

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
                .Where(x => x.Type == AccountType.Revenue)
                .Sum(x => x.DisplayAmount);

        var augustRevenue =
            august.Balances
                .Where(x => x.Type == AccountType.Revenue)
                .Sum(x => x.DisplayAmount);

        Assert.Equal(55.56m, julyRevenue);
Assert.Equal(44.44m, augustRevenue);
    }

    [Fact]
    public async Task SupplierCreditReversedInLaterPeriod_ReversesCreditOnlyInReversalPeriod()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill =
            await test.Purchasing.PostBillAsync(
                test.UserId,
                new SupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "PL-CREDIT-BILL-001",
                    BillDate: new DateOnly(2026, 7, 15),
                    DueDate: new DateOnly(2026, 8, 14),
                    Lines:
                    [
                        new SupplierBillLineRequest(
                            Description: "July credit-note expense",
                            Quantity: 1m,
                            UnitPrice: 40m,
                            VatTreatment: VatTreatment.Standard,
                            ExpenseAccountId:
                                test.Account("6000").Id)
                    ]));

        var credits =
            new SupplierCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var credit =
            await credits.CreateAsync(
                test.UserId,
                new SupplierCreditNoteRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierBillId: bill.Id,
                    Date: new DateOnly(2026, 7, 20),
                    Reason: "July supplier credit",
                    Amount: 20m,
                    ReturnTrackedItems: false));

        await credits.ReverseAsync(
            test.UserId,
            test.Organisation.Id,
            credit.Id,
            new DateOnly(2026, 8, 5),
            "August reversal");

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
                .Where(x => x.Type == AccountType.Expense)
                .Sum(x => x.DisplayAmount);

        var augustExpenses =
            august.Balances
                .Where(x => x.Type == AccountType.Expense)
                .Sum(x => x.DisplayAmount);

        Assert.Equal(22.22m, julyExpenses);
Assert.Equal(17.78m, augustExpenses);
    }
}