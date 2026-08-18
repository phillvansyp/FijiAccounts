using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class TrialBalanceAccountingTests
{
    [Fact]
    public async Task PostedTransactions_ProduceBalancedTrialBalance()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        // -------------------------------------------------
        // 1. Post a sales invoice
        // -------------------------------------------------

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

        // -------------------------------------------------
        // 2. Post a supplier bill
        // -------------------------------------------------

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
                        ExpenseAccountId: test.Account("6000").Id)
                ]));

        // -------------------------------------------------
        // 3. Reproduce the Trial Balance calculation
        //    used by Reports.razor
        // -------------------------------------------------

        var rows = await test.Db.PostedJournalLines
            .AsNoTracking()
            .Where(x =>
                x.PostedJournal.OrganisationId ==
                    test.Organisation.Id &&
                x.PostedJournal.EntryDate <=
                    new DateOnly(2026, 8, 18))
            .GroupBy(x => new
            {
                x.LedgerAccount.Code,
                x.LedgerAccount.Name
            })
            .Select(x => new
            {
                x.Key.Code,
                x.Key.Name,
                Debit = x.Sum(y => y.Debit),
                Credit = x.Sum(y => y.Credit)
            })
            .ToListAsync();

        // -------------------------------------------------
        // 4. Trial balance MUST agree
        // -------------------------------------------------

        var totalDebit = rows.Sum(x => x.Debit);
        var totalCredit = rows.Sum(x => x.Credit);

        Assert.Equal(totalDebit, totalCredit);

        // -------------------------------------------------
        // 5. Ensure important accounts are represented
        // -------------------------------------------------

        Assert.Contains(rows, x => x.Code == "1100");
        Assert.Contains(rows, x => x.Code == "4000");
        Assert.Contains(rows, x => x.Code == "2000");
        Assert.Contains(rows, x => x.Code == "6000");

        // -------------------------------------------------
        // 6. VAT accounts must also exist
        // -------------------------------------------------

        Assert.Contains(
            rows,
            x => x.Code == "2100" || x.Code == "1200");
    }
}