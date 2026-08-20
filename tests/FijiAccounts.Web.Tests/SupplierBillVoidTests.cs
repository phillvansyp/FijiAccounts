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
    [Fact]
    public async Task VoidSupplierBill_WithTrackedPurchase_RestoresOriginalInventoryPosition()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var catalog =
            new ProductCatalogService(
                test.Db,
                test.Access);

        var inventory =
            new InventoryService(
                test.Db,
                test.Access,
                test.Posting);

        var item =
            await catalog.CreateAsync(
                test.UserId,
                new ProductItemRequest(
                    OrganisationId: test.Organisation.Id,
                    Code: "VOID-STOCK-001",
                    Name: "Supplier Bill Void Stock",
                    Description: "Tracked supplier bill void regression",
                    Kind: ProductKind.TrackedItem,
                    SalePrice: 50m,
                    PurchasePrice: 20m,
                    SaleTaxTreatment: VatTreatment.Standard,
                    PurchaseTaxTreatment: VatTreatment.Standard,
                    RevenueAccountId: test.Account("4000").Id,
                    ExpenseAccountId: test.Account("5000").Id));

        // Existing stock: 1 unit @ $30.
        await inventory.AdjustAsync(
            test.UserId,
            new InventoryAdjustmentRequest(
                OrganisationId: test.Organisation.Id,
                ProductItemId: item.Id,
                Date: new DateOnly(2026, 8, 17),
                QuantityChange: 1m,
                UnitCost: 30m,
                ReorderLevel: 0m,
                InventoryAccountId: test.Account("1200").Id,
                AdjustmentAccountId: test.Account("5000").Id,
                Reference: "VOID-STOCK-OPENING",
                Note: "Existing stock"));

        var bill =
            await test.Purchasing.PostBillAsync(
                test.UserId,
                new SupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "SUP-VOID-STOCK-001",
                    BillDate: new DateOnly(2026, 8, 18),
                    DueDate: new DateOnly(2026, 9, 17),
                    Lines:
                    [
                        new SupplierBillLineRequest(
                            Description: "Tracked supplier purchase",
                            Quantity: 2m,
                            UnitPrice: 20m,
                            VatTreatment: VatTreatment.Standard,
                            ExpenseAccountId: test.Account("1200").Id,
                            ProductItemId: item.Id)
                    ]));

        var afterBill =
            await test.Db.ProductItems
                .AsNoTracking()
                .SingleAsync(x => x.Id == item.Id);

        Assert.Equal(3m, afterBill.QuantityOnHand);
        Assert.Equal(23.3333m, afterBill.AverageCost);

        await test.Purchasing.VoidBillAsync(
            test.UserId,
            test.Organisation.Id,
            bill.Id,
            new DateOnly(2026, 8, 19),
            "Void tracked supplier purchase");

        var afterVoid =
            await test.Db.ProductItems
                .AsNoTracking()
                .SingleAsync(x => x.Id == item.Id);

        Assert.Equal(1m, afterVoid.QuantityOnHand);
        Assert.Equal(30m, afterVoid.AverageCost);

        var purchaseReturn =
            await test.Db.InventoryMovements
                .AsNoTracking()
                .SingleAsync(
                    x =>
                        x.ProductItemId == item.Id &&
                        x.Type == InventoryMovementType.PurchaseReturn);

        Assert.Equal(-2m, purchaseReturn.QuantityChange);
        Assert.Equal(20m, purchaseReturn.UnitCost);
        Assert.Equal(-40m, purchaseReturn.ValueChange);

        var reloadedBill =
            await test.Db.SupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == bill.Id);

        Assert.Equal(BillStatus.Voided, reloadedBill.Status);
    }

    [Fact]
    public async Task VoidSupplierBill_WhenReceivedTrackedUnitsAreNoLongerOnHand_IsRejectedWithoutMutation()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var catalog =
            new ProductCatalogService(
                test.Db,
                test.Access);

        var inventory =
            new InventoryService(
                test.Db,
                test.Access,
                test.Posting);

        var item =
            await catalog.CreateAsync(
                test.UserId,
                new ProductItemRequest(
                    OrganisationId: test.Organisation.Id,
                    Code: "VOID-STOCK-002",
                    Name: "Consumed Supplier Stock",
                    Description: "Supplier bill void rejection regression",
                    Kind: ProductKind.TrackedItem,
                    SalePrice: 50m,
                    PurchasePrice: 20m,
                    SaleTaxTreatment: VatTreatment.Standard,
                    PurchaseTaxTreatment: VatTreatment.Standard,
                    RevenueAccountId: test.Account("4000").Id,
                    ExpenseAccountId: test.Account("5000").Id));
        
                // Establish the tracked item's inventory accounts and
        // one unit of pre-existing stock.
        await inventory.AdjustAsync(
            test.UserId,
            new InventoryAdjustmentRequest(
                OrganisationId: test.Organisation.Id,
                ProductItemId: item.Id,
                Date: new DateOnly(2026, 8, 17),
                QuantityChange: 1m,
                UnitCost: 30m,
                ReorderLevel: 0m,
                InventoryAccountId: test.Account("1200").Id,
                AdjustmentAccountId: test.Account("5000").Id,
                Reference: "VOID-STOCK-002-OPENING",
                Note: "Existing stock"));

        var bill =
            await test.Purchasing.PostBillAsync(
                test.UserId,
                new SupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "SUP-VOID-STOCK-002",
                    BillDate: new DateOnly(2026, 8, 18),
                    DueDate: new DateOnly(2026, 9, 17),
                    Lines:
                    [
                        new SupplierBillLineRequest(
                            Description: "Tracked supplier purchase",
                            Quantity: 2m,
                            UnitPrice: 20m,
                            VatTreatment: VatTreatment.Standard,
                            ExpenseAccountId: test.Account("1200").Id,
                            ProductItemId: item.Id)
                    ]));

                // Consume two units so only one remains on hand,
        // which is fewer than the two units received by the bill.
        await inventory.AdjustAsync(
            test.UserId,
            new InventoryAdjustmentRequest(
                OrganisationId: test.Organisation.Id,
                ProductItemId: item.Id,
                Date: new DateOnly(2026, 8, 19),
                QuantityChange: -2m,
                UnitCost: 999m,
                ReorderLevel: 0m,
                InventoryAccountId: test.Account("1200").Id,
                AdjustmentAccountId: test.Account("5000").Id,
                Reference: "VOID-STOCK-CONSUMED",
                Note: "Consume received stock"));

        var beforeVoid =
            await test.Db.ProductItems
                .AsNoTracking()
                .SingleAsync(x => x.Id == item.Id);

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var movementCountBefore =
            await test.Db.InventoryMovements.CountAsync();

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    test.Purchasing.VoidBillAsync(
                        test.UserId,
                        test.Organisation.Id,
                        bill.Id,
                        new DateOnly(2026, 8, 20),
                        "Should be rejected"));

        Assert.Contains(
            "no longer has all received units",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        var afterRejectedVoid =
            await test.Db.ProductItems
                .AsNoTracking()
                .SingleAsync(x => x.Id == item.Id);

        Assert.Equal(
            beforeVoid.QuantityOnHand,
            afterRejectedVoid.QuantityOnHand);

        Assert.Equal(
            beforeVoid.AverageCost,
            afterRejectedVoid.AverageCost);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());

        Assert.Equal(
            movementCountBefore,
            await test.Db.InventoryMovements.CountAsync());

        var reloadedBill =
            await test.Db.SupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == bill.Id);

        Assert.NotEqual(
            BillStatus.Voided,
            reloadedBill.Status);

        Assert.False(
            await test.Db.InventoryMovements
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.ProductItemId == item.Id &&
                        x.Type == InventoryMovementType.PurchaseReturn));
    }

    [Fact]
public async Task VoidSupplierBill_WhenVoidDateIsInsideLockedAccountingPeriod_IsRejectedWithoutMutation()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var bill =
        await test.Purchasing.PostBillAsync(
            test.UserId,
            new SupplierBillRequest(
                OrganisationId: test.Organisation.Id,
                SupplierId: test.Supplier.Id,
                SupplierReference: "SUP-VOID-LOCKED-001",
                BillDate: new DateOnly(2026, 8, 18),
                DueDate: new DateOnly(2026, 9, 17),
                Lines:
                [
                    new SupplierBillLineRequest(
                        Description: "Locked period supplier bill",
                        Quantity: 1m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        ExpenseAccountId: test.Account("6500").Id)
                ]));

    test.Db.AccountingPeriods.Add(
        new AccountingPeriod
{
    OrganisationId = test.Organisation.Id,
    Name = "August 2026",
    StartsOn = new DateOnly(2026, 8, 1),
    EndsOn = new DateOnly(2026, 8, 31),
    IsLocked = true
});

    await test.Db.SaveChangesAsync();

    var journalCountBefore =
        await test.Db.PostedJournals.CountAsync();

    var voidCountBefore =
        await test.Db.SupplierBillVoids.CountAsync();

    var auditCountBefore =
        await test.Db.AuditEvents.CountAsync();

    var exception =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                test.Purchasing.VoidBillAsync(
                    test.UserId,
                    test.Organisation.Id,
                    bill.Id,
                    new DateOnly(2026, 8, 20),
                    "Locked period regression"));

    Assert.Contains(
        "locked",
        exception.Message,
        StringComparison.OrdinalIgnoreCase);

    var reloadedBill =
        await test.Db.SupplierBills
            .AsNoTracking()
            .SingleAsync(x => x.Id == bill.Id);

    Assert.Equal(
        BillStatus.Posted,
        reloadedBill.Status);

    Assert.Equal(
        journalCountBefore,
        await test.Db.PostedJournals.CountAsync());

    Assert.Equal(
        voidCountBefore,
        await test.Db.SupplierBillVoids.CountAsync());

    Assert.Equal(
        auditCountBefore,
        await test.Db.AuditEvents.CountAsync());
}
}
