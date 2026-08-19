using Microsoft.EntityFrameworkCore;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class SupplierBillAccountingTests
{
    [Fact]
    public async Task PostBill_WithFijiVat_PostsBalancedApExpenseAndVatJournal()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill =
            await test.Purchasing.PostBillAsync(
                test.UserId,
                new SupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "SUP-TEST-001",
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

        Assert.NotEqual(Guid.Empty, bill.Id);
        Assert.NotEqual(Guid.Empty, bill.PostedJournalId);

        Assert.Equal(100m, bill.Subtotal);
        Assert.Equal(12.50m, bill.VatTotal);
        Assert.Equal(112.50m, bill.Total);

        var journal =
    await test.LoadJournalAsync(
        bill.PostedJournalId);

        var totalDebit =
            journal.Lines.Sum(x => x.Debit);

        var totalCredit =
            journal.Lines.Sum(x => x.Credit);

        Assert.Equal(totalDebit, totalCredit);

        Assert.Equal(112.50m, totalDebit);
        Assert.Equal(112.50m, totalCredit);

        var expense =
            journal.Lines.Single(
                x => x.LedgerAccount.Code == "6500");

        var vatReceivable =
            journal.Lines.Single(
                x => x.LedgerAccount.Code == "1150");

        var payable =
            journal.Lines.Single(
                x => x.LedgerAccount.Code == "2000");

        Assert.Equal(100m, expense.Debit);
        Assert.Equal(0m, expense.Credit);

        Assert.Equal(12.50m, vatReceivable.Debit);
        Assert.Equal(0m, vatReceivable.Credit);

        Assert.Equal(0m, payable.Debit);
        Assert.Equal(112.50m, payable.Credit);
    }

    [Fact]
public async Task PostBill_WhenVatReceivableControlIsMissing_FailsWithoutRecreatingIt()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var vatReceivable =
        test.Account("1150");

    test.Db.LedgerAccounts.Remove(vatReceivable);
    await test.Db.SaveChangesAsync();

    var exception =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                test.Purchasing.PostBillAsync(
                    test.UserId,
                    new SupplierBillRequest(
                        OrganisationId: test.Organisation.Id,
                        SupplierId: test.Supplier.Id,
                        SupplierReference: "SUP-MISSING-1150",
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
                        ])));

    Assert.Equal(
        "VAT Receivable (1150) and Accounts Payable (2000) are required.",
        exception.Message);

    var recreated =
        await test.Db.LedgerAccounts
            .AnyAsync(
                x =>
                    x.OrganisationId == test.Organisation.Id &&
                    x.Code == "1150");

    Assert.False(recreated);
}
}