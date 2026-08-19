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

        [Fact]
    public async Task InvoiceVoidedInLaterPeriod_ChangesTrialBalanceOnlyFromVoidDate()
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

        var julyReceivables =
            july.TrialBalance
                .Where(x => x.Code == "1100")
                .Sum(x => x.Debit - x.Credit);

        var julyRevenue =
            july.TrialBalance
                .Where(x => x.Code == "4000")
                .Sum(x => x.Credit - x.Debit);

        var augustReceivables =
            august.TrialBalance
                .Where(x => x.Code == "1100")
                .Sum(x => x.Debit - x.Credit);

        var augustRevenue =
            august.TrialBalance
                .Where(x => x.Code == "4000")
                .Sum(x => x.Credit - x.Debit);

        Assert.Equal(112.50m, julyReceivables);
        Assert.Equal(100m, julyRevenue);

        Assert.Equal(0m, augustReceivables);
        Assert.Equal(0m, augustRevenue);

        Assert.Equal(
            july.TrialBalance.Sum(x => x.Debit),
            july.TrialBalance.Sum(x => x.Credit));

        Assert.Equal(
            august.TrialBalance.Sum(x => x.Debit),
            august.TrialBalance.Sum(x => x.Credit));
    }

        [Fact]
    public async Task SupplierBillVoidedInLaterPeriod_ChangesTrialBalanceOnlyFromVoidDate()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill =
            await test.Purchasing.PostBillAsync(
                test.UserId,
                new SupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "TB-VOID-BILL-001",
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

        var julyPayables =
            july.TrialBalance
                .Where(x => x.Code == "2000")
                .Sum(x => x.Credit - x.Debit);

        var julyExpenses =
            july.TrialBalance
                .Where(x => x.Code == "6000")
                .Sum(x => x.Debit - x.Credit);

        var augustPayables =
            august.TrialBalance
                .Where(x => x.Code == "2000")
                .Sum(x => x.Credit - x.Debit);

        var augustExpenses =
            august.TrialBalance
                .Where(x => x.Code == "6000")
                .Sum(x => x.Debit - x.Credit);

        Assert.Equal(45m, julyPayables);
        Assert.Equal(40m, julyExpenses);

        Assert.Equal(0m, augustPayables);
        Assert.Equal(0m, augustExpenses);

        Assert.Equal(
            july.TrialBalance.Sum(x => x.Debit),
            july.TrialBalance.Sum(x => x.Credit));

        Assert.Equal(
            august.TrialBalance.Sum(x => x.Debit),
            august.TrialBalance.Sum(x => x.Credit));
    }

        [Fact]
    public async Task SalesCreditReversedInLaterPeriod_ChangesTrialBalanceOnlyFromReversalDate()
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

        var julyReceivables =
            july.TrialBalance
                .Where(x => x.Code == "1100")
                .Sum(x => x.Debit - x.Credit);

        var julyRevenue =
            july.TrialBalance
                .Where(x => x.Code == "4000")
                .Sum(x => x.Credit - x.Debit);

        var augustReceivables =
            august.TrialBalance
                .Where(x => x.Code == "1100")
                .Sum(x => x.Debit - x.Credit);

        var augustRevenue =
            august.TrialBalance
                .Where(x => x.Code == "4000")
                .Sum(x => x.Credit - x.Debit);

        Assert.Equal(62.50m, julyReceivables);
Assert.Equal(55.56m, julyRevenue);

Assert.Equal(112.50m, augustReceivables);
Assert.Equal(100m, augustRevenue);

        Assert.Equal(
            july.TrialBalance.Sum(x => x.Debit),
            july.TrialBalance.Sum(x => x.Credit));

        Assert.Equal(
            august.TrialBalance.Sum(x => x.Debit),
            august.TrialBalance.Sum(x => x.Credit));
    }

    [Fact]
    public async Task SupplierCreditReversedInLaterPeriod_ChangesTrialBalanceOnlyFromReversalDate()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill =
            await test.Purchasing.PostBillAsync(
                test.UserId,
                new SupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "TB-CREDIT-BILL-001",
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

        var julyPayables =
            july.TrialBalance
                .Where(x => x.Code == "2000")
                .Sum(x => x.Credit - x.Debit);

        var julyExpenses =
            july.TrialBalance
                .Where(x => x.Code == "6000")
                .Sum(x => x.Debit - x.Credit);

        var augustPayables =
            august.TrialBalance
                .Where(x => x.Code == "2000")
                .Sum(x => x.Credit - x.Debit);

        var augustExpenses =
            august.TrialBalance
                .Where(x => x.Code == "6000")
                .Sum(x => x.Debit - x.Credit);

        Assert.Equal(25m, julyPayables);
Assert.Equal(22.22m, julyExpenses);

Assert.Equal(45m, augustPayables);
Assert.Equal(40m, augustExpenses);

        Assert.Equal(
            july.TrialBalance.Sum(x => x.Debit),
            july.TrialBalance.Sum(x => x.Credit));

        Assert.Equal(
            august.TrialBalance.Sum(x => x.Debit),
            august.TrialBalance.Sum(x => x.Credit));
    }
}