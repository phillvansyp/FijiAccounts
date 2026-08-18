using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SupplierBillVoidTests
{
    [Fact]
    public async Task VoidSupplierBill_PostsExactReversalAndRestoresBalances()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill =
            await test.Purchasing.PostBillAsync(
                test.UserId,
                new SupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "SUP-VOID-001",
                    BillDate: new DateOnly(2026, 8, 18),
                    DueDate: new DateOnly(2026, 9, 17),
                    Lines:
                    [
                        new SupplierBillLineRequest(
                            Description: "Office supplies",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            ExpenseAccountId: test.Account("6500").Id)
                    ]));

        var originalJournal =
            await test.LoadJournalAsync(
                bill.PostedJournalId);

        await test.Purchasing.VoidBillAsync(
            test.UserId,
            test.Organisation.Id,
            bill.Id,
            new DateOnly(2026, 8, 19),
            "Regression test void");

        var reloadedBill =
            await test.Db.SupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == bill.Id);

        Assert.Equal(
            BillStatus.Voided,
            reloadedBill.Status);

        var journals =
            await test.Db.PostedJournals
                .AsNoTracking()
                .Include(x => x.Lines)
                .Where(x =>
                    x.OrganisationId == test.Organisation.Id)
                .ToListAsync();

        Assert.Equal(2, journals.Count);

        var reversalJournal =
            journals.Single(
                x => x.Id != bill.PostedJournalId);

        foreach (var originalLine in originalJournal.Lines)
        {
            var reversalLine =
                reversalJournal.Lines.Single(
                    x =>
                        x.LedgerAccountId ==
                        originalLine.LedgerAccountId);

            Assert.Equal(
                originalLine.Debit,
                reversalLine.Credit);

            Assert.Equal(
                originalLine.Credit,
                reversalLine.Debit);
        }

        Assert.Equal(
            0m,
            await test.AccountBalanceAsync("6500"));

        Assert.Equal(
            0m,
            await test.AccountBalanceAsync("1150"));

        Assert.Equal(
            0m,
            await test.AccountBalanceAsync("2000"));
    }
}