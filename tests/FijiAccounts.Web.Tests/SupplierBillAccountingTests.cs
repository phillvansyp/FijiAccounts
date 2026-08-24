using Microsoft.EntityFrameworkCore;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
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
    public async Task PostBill_WithVatIncludedPrices_ExtractsVatFromEnteredTotal()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();

        var bill = await test.Purchasing.PostBillAsync(
            test.UserId,
            new SupplierBillRequest(
                OrganisationId: test.Organisation.Id,
                SupplierId: test.Supplier.Id,
                SupplierReference: "SUP-VAT-INCLUSIVE",
                BillDate: new DateOnly(2026, 8, 18),
                DueDate: new DateOnly(2026, 9, 17),
                Lines:
                [
                    new SupplierBillLineRequest(
                        Description: "VAT-inclusive supplies",
                        Quantity: 1m,
                        UnitPrice: 112.50m,
                        VatTreatment: VatTreatment.Standard,
                        ExpenseAccountId: test.Account("6500").Id)
                ],
                AmountsIncludeVat: true));

        Assert.Equal(100m, bill.Subtotal);
        Assert.Equal(12.50m, bill.VatTotal);
        Assert.Equal(112.50m, bill.Total);
        Assert.Equal(100m, bill.Lines.Single().UnitPrice);

        var journal = await test.LoadJournalAsync(bill.PostedJournalId);
        Assert.Equal(100m, journal.Lines.Single(x => x.LedgerAccount.Code == "6500").Debit);
        Assert.Equal(12.50m, journal.Lines.Single(x => x.LedgerAccount.Code == "1150").Debit);
        Assert.Equal(112.50m, journal.Lines.Single(x => x.LedgerAccount.Code == "2000").Credit);
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
    "VAT Receivable (1150) must be an active Asset account.",
    exception.Message);

    var recreated =
        await test.Db.LedgerAccounts
            .AnyAsync(
                x =>
                    x.OrganisationId == test.Organisation.Id &&
                    x.Code == "1150");

    Assert.False(recreated);
}

[Fact]
public async Task PostBill_WhenVatReceivableControlIsInactive_IsRejected()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var vatReceivable = test.Account("1150");
    vatReceivable.IsActive = false;

    await test.Db.SaveChangesAsync();

    var journalCountBefore =
        await test.Db.PostedJournals.CountAsync();

    var ex =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                test.Purchasing.PostBillAsync(
                    test.UserId,
                    new SupplierBillRequest(
                        OrganisationId: test.Organisation.Id,
                        SupplierId: test.Supplier.Id,
                        SupplierReference: "SUP-INACTIVE-1150",
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

    Assert.Contains(
        "1150",
        ex.Message,
        StringComparison.OrdinalIgnoreCase);

    Assert.Equal(
        journalCountBefore,
        await test.Db.PostedJournals.CountAsync());
}

[Fact]
public async Task PostBill_WhenVatReceivableControlHasWrongType_IsRejected()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var vatReceivable = test.Account("1150");
    vatReceivable.Type = AccountType.Liability;

    await test.Db.SaveChangesAsync();

    var journalCountBefore =
        await test.Db.PostedJournals.CountAsync();

    var ex =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                test.Purchasing.PostBillAsync(
                    test.UserId,
                    new SupplierBillRequest(
                        OrganisationId: test.Organisation.Id,
                        SupplierId: test.Supplier.Id,
                        SupplierReference: "SUP-WRONG-1150",
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

    Assert.Contains(
        "1150",
        ex.Message,
        StringComparison.OrdinalIgnoreCase);

    Assert.Equal(
        journalCountBefore,
        await test.Db.PostedJournals.CountAsync());
}

[Fact]
public async Task PostBill_WhenAccountsPayableControlIsInactive_IsRejected()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var payable = test.Account("2000");
    payable.IsActive = false;

    await test.Db.SaveChangesAsync();

    var journalCountBefore =
        await test.Db.PostedJournals.CountAsync();

    var ex =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                test.Purchasing.PostBillAsync(
                    test.UserId,
                    new SupplierBillRequest(
                        OrganisationId: test.Organisation.Id,
                        SupplierId: test.Supplier.Id,
                        SupplierReference: "SUP-INACTIVE-2000",
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

    Assert.Contains(
        "2000",
        ex.Message,
        StringComparison.OrdinalIgnoreCase);

    Assert.Equal(
        journalCountBefore,
        await test.Db.PostedJournals.CountAsync());
}

[Fact]
public async Task PostBill_WhenAccountsPayableControlHasWrongType_IsRejected()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var payable = test.Account("2000");
    payable.Type = AccountType.Asset;

    await test.Db.SaveChangesAsync();

    var journalCountBefore =
        await test.Db.PostedJournals.CountAsync();

    var ex =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                test.Purchasing.PostBillAsync(
                    test.UserId,
                    new SupplierBillRequest(
                        OrganisationId: test.Organisation.Id,
                        SupplierId: test.Supplier.Id,
                        SupplierReference: "SUP-WRONG-2000",
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

    Assert.Contains(
        "2000",
        ex.Message,
        StringComparison.OrdinalIgnoreCase);

    Assert.Equal(
        journalCountBefore,
        await test.Db.PostedJournals.CountAsync());
}

    [Fact]
public async Task PostBill_WhenTrackedItemInventoryAccountHasWrongType_IsRejected()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var inventoryAccount =
        new LedgerAccount
        {
            OrganisationId = test.Organisation.Id,
            Code = "1210",
            Name = "Inventory Control Test",
            Type = AccountType.Asset,
            IsActive = true
        };

    test.Db.LedgerAccounts.Add(inventoryAccount);
    await test.Db.SaveChangesAsync();

    var item =
        new ProductItem
        {
            OrganisationId = test.Organisation.Id,
            Code = "TRACK-PURCHASE-001",
            Name = "Tracked Purchase Test",
            Kind = ProductKind.TrackedItem,
            InventoryAccountId = inventoryAccount.Id
        };

    test.Db.ProductItems.Add(item);
    await test.Db.SaveChangesAsync();

    inventoryAccount.Type = AccountType.Expense;
    await test.Db.SaveChangesAsync();

    var journalCountBefore =
        await test.Db.PostedJournals.CountAsync();

    var ex =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                test.Purchasing.PostBillAsync(
                    test.UserId,
                    new SupplierBillRequest(
                        OrganisationId: test.Organisation.Id,
                        SupplierId: test.Supplier.Id,
                        SupplierReference: "SUP-TRACK-WRONG-INVENTORY",
                        BillDate: new DateOnly(2026, 8, 20),
                        DueDate: new DateOnly(2026, 9, 19),
                        Lines:
                        [
                            new SupplierBillLineRequest(
                                Description: "Tracked purchase",
                                Quantity: 1m,
                                UnitPrice: 100m,
                                VatTreatment: VatTreatment.Standard,
                                ExpenseAccountId: inventoryAccount.Id,
                                ProductItemId: item.Id)
                        ])));

    Assert.Contains(
        "1210",
        ex.Message,
        StringComparison.OrdinalIgnoreCase);

    Assert.Equal(
        journalCountBefore,
        await test.Db.PostedJournals.CountAsync());
}
}
