using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

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
                        RevenueAccountId: test.Account("4000").Id)
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
                        ExpenseAccountId: test.Account("6000").Id)
                ]));

        var all =
            await test.Db.PostedJournalLines
                .AsNoTracking()
                .Where(x =>
                    x.PostedJournal.OrganisationId ==
                        test.Organisation.Id &&
                    x.PostedJournal.EntryDate <=
                        new DateOnly(2026, 8, 31))
                .GroupBy(x => new
                {
                    x.LedgerAccount.Code,
                    x.LedgerAccount.Type
                })
                .Select(x => new
                {
                    x.Key.Code,
                    x.Key.Type,
                    Debit = x.Sum(y => y.Debit),
                    Credit = x.Sum(y => y.Credit)
                })
                .ToListAsync();

        var assets =
            all.Where(x => x.Type == AccountType.Asset)
               .Sum(x => x.Debit - x.Credit);

        var liabilities =
            all.Where(x => x.Type == AccountType.Liability)
               .Sum(x => x.Credit - x.Debit);

        var equity =
            all.Where(x => x.Type == AccountType.Equity)
               .Sum(x => x.Credit - x.Debit);

        var revenue =
            all.Where(x => x.Type == AccountType.Revenue)
               .Sum(x => x.Credit - x.Debit);

        var expenses =
            all.Where(x => x.Type == AccountType.Expense)
               .Sum(x => x.Debit - x.Credit);

        var currentProfit = revenue - expenses;

        Assert.Equal(
            assets,
            liabilities + equity + currentProfit);

        Assert.Equal(60m, currentProfit);
    }
}