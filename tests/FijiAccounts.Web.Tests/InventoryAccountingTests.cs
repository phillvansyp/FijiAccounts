using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class InventoryAccountingTests
{
    [Fact]
    public async Task WeightedAverageInventory_AdjustmentsUseCorrectCostAndJournalValue()
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
                    Code: "STOCK-001",
                    Name: "Tracked Test Item",
                    Description: "Inventory regression test",
                    Kind: ProductKind.TrackedItem,
                    SalePrice: 50m,
                    PurchasePrice: 20m,
                    SaleTaxTreatment: VatTreatment.Standard,
                    PurchaseTaxTreatment: VatTreatment.Standard,
                    RevenueAccountId: test.Account("4000").Id,
                    ExpenseAccountId: test.Account("5000").Id));

        // -------------------------------------------------
        // First receipt:
        // 10 units @ $20
        // -------------------------------------------------

        await inventory.AdjustAsync(
            test.UserId,
            new InventoryAdjustmentRequest(
                OrganisationId: test.Organisation.Id,
                ProductItemId: item.Id,
                Date: new DateOnly(2026, 8, 18),
                QuantityChange: 10m,
                UnitCost: 20m,
                ReorderLevel: 2m,
                InventoryAccountId: test.Account("1200").Id,
                AdjustmentAccountId: test.Account("5000").Id,
                Reference: "STOCK-IN-001",
                Note: "Initial stock"));

        var afterFirstReceipt =
            await test.Db.ProductItems
                .AsNoTracking()
                .SingleAsync(x => x.Id == item.Id);

        Assert.Equal(10m, afterFirstReceipt.QuantityOnHand);
        Assert.Equal(20m, afterFirstReceipt.AverageCost);

        Assert.Equal(
            200m,
            await test.AccountBalanceAsync("1200"));

        Assert.Equal(
            -200m,
            await test.AccountBalanceAsync("5000"));

        // -------------------------------------------------
        // Second receipt:
        // 10 units @ $30
        //
        // Weighted average:
        // (10 x 20 + 10 x 30) / 20 = $25
        // -------------------------------------------------

        await inventory.AdjustAsync(
            test.UserId,
            new InventoryAdjustmentRequest(
                OrganisationId: test.Organisation.Id,
                ProductItemId: item.Id,
                Date: new DateOnly(2026, 8, 19),
                QuantityChange: 10m,
                UnitCost: 30m,
                ReorderLevel: 2m,
                InventoryAccountId: test.Account("1200").Id,
                AdjustmentAccountId: test.Account("5000").Id,
                Reference: "STOCK-IN-002",
                Note: "Second stock receipt"));

        var afterSecondReceipt =
            await test.Db.ProductItems
                .AsNoTracking()
                .SingleAsync(x => x.Id == item.Id);

        Assert.Equal(20m, afterSecondReceipt.QuantityOnHand);
        Assert.Equal(25m, afterSecondReceipt.AverageCost);

        Assert.Equal(
            500m,
            await test.AccountBalanceAsync("1200"));

        Assert.Equal(
            -500m,
            await test.AccountBalanceAsync("5000"));

        // -------------------------------------------------
        // Stock reduction:
        // remove 4 units
        //
        // Service must ignore supplied UnitCost on a decrease
        // and use current weighted average of $25.
        //
        // 4 x $25 = $100
        // -------------------------------------------------

        var reduction =
            await inventory.AdjustAsync(
                test.UserId,
                new InventoryAdjustmentRequest(
                    OrganisationId: test.Organisation.Id,
                    ProductItemId: item.Id,
                    Date: new DateOnly(2026, 8, 20),
                    QuantityChange: -4m,
                    UnitCost: 999m,
                    ReorderLevel: 2m,
                    InventoryAccountId: test.Account("1200").Id,
                    AdjustmentAccountId: test.Account("5000").Id,
                    Reference: "STOCK-OUT-001",
                    Note: "Stock reduction"));

        var finalItem =
            await test.Db.ProductItems
                .AsNoTracking()
                .SingleAsync(x => x.Id == item.Id);

        Assert.Equal(16m, finalItem.QuantityOnHand);
        Assert.Equal(25m, finalItem.AverageCost);

        Assert.Equal(
            InventoryMovementType.AdjustmentDecrease,
            reduction.Type);

        Assert.Equal(-4m, reduction.QuantityChange);
        Assert.Equal(25m, reduction.UnitCost);
        Assert.Equal(-100m, reduction.ValueChange);

        // Inventory asset falls from $500 to $400.
        Assert.Equal(
            400m,
            await test.AccountBalanceAsync("1200"));

        // Cost-of-sales/adjustment account reverses $100
        // of the earlier inventory increases.
        Assert.Equal(
            -400m,
            await test.AccountBalanceAsync("5000"));

        var reductionJournal =
            await test.LoadJournalAsync(
                reduction.PostedJournalId);

        var inventoryLine =
            reductionJournal.Lines.Single(
                x => x.LedgerAccount.Code == "1200");

        var adjustmentLine =
            reductionJournal.Lines.Single(
                x => x.LedgerAccount.Code == "5000");

        Assert.Equal(0m, inventoryLine.Debit);
        Assert.Equal(100m, inventoryLine.Credit);

        Assert.Equal(100m, adjustmentLine.Debit);
        Assert.Equal(0m, adjustmentLine.Credit);

        Assert.Equal(
            reductionJournal.Lines.Sum(x => x.Debit),
            reductionJournal.Lines.Sum(x => x.Credit));
    }

    [Fact]
    public async Task InventoryAdjustment_CannotMakeStockNegative()
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
                    Code: "STOCK-NEG-001",
                    Name: "Negative Stock Test",
                    Description: null,
                    Kind: ProductKind.TrackedItem,
                    SalePrice: 50m,
                    PurchasePrice: 20m,
                    SaleTaxTreatment: VatTreatment.Standard,
                    PurchaseTaxTreatment: VatTreatment.Standard,
                    RevenueAccountId: test.Account("4000").Id,
                    ExpenseAccountId: test.Account("5000").Id));

        var error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => inventory.AdjustAsync(
                    test.UserId,
                    new InventoryAdjustmentRequest(
                        OrganisationId: test.Organisation.Id,
                        ProductItemId: item.Id,
                        Date: new DateOnly(2026, 8, 18),
                        QuantityChange: -1m,
                        UnitCost: 20m,
                        ReorderLevel: 0m,
                        InventoryAccountId: test.Account("1200").Id,
                        AdjustmentAccountId: test.Account("5000").Id,
                        Reference: "NEG-001",
                        Note: null)));

        Assert.Contains(
            "negative",
            error.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            0m,
            await test.AccountBalanceAsync("1200"));

        Assert.Empty(
            await test.Db.InventoryMovements
                .AsNoTracking()
                .Where(x => x.ProductItemId == item.Id)
                .ToListAsync());
    }
}