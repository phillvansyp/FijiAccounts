using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SupplierCreditNoteAccountingTests
{
    [Fact]
    public async Task CreateAsync_WhenVatReceivableControlAccountIsInactive_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill = await CreateBillAsync(test);

        var vatReceivable = test.Account("1150");
        vatReceivable.IsActive = false;
        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var service =
            new SupplierCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new SupplierCreditNoteRequest(
                            OrganisationId: test.Organisation.Id,
                            SupplierBillId: bill.Id,
                            Date: new DateOnly(2026, 8, 20),
                            Reason: "Inactive VAT receivable control test",
                            Amount: 50m,
                            ReturnTrackedItems: false)));

        Assert.Contains(
            "1150",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_WhenVatReceivableControlAccountHasWrongType_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill = await CreateBillAsync(test);

        var vatReceivable = test.Account("1150");
        vatReceivable.Type = AccountType.Liability;
        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var service =
            new SupplierCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new SupplierCreditNoteRequest(
                            OrganisationId: test.Organisation.Id,
                            SupplierBillId: bill.Id,
                            Date: new DateOnly(2026, 8, 20),
                            Reason: "Invalid VAT receivable control type test",
                            Amount: 50m,
                            ReturnTrackedItems: false)));

        Assert.Contains(
            "1150",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_WhenAccountsPayableControlAccountIsInactive_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill = await CreateBillAsync(test);

        var accountsPayable = test.Account("2000");
        accountsPayable.IsActive = false;
        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var service =
            new SupplierCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new SupplierCreditNoteRequest(
                            OrganisationId: test.Organisation.Id,
                            SupplierBillId: bill.Id,
                            Date: new DateOnly(2026, 8, 20),
                            Reason: "Inactive AP control test",
                            Amount: 50m,
                            ReturnTrackedItems: false)));

        Assert.Contains(
            "2000",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());
    }

    [Fact]
public async Task CreateAsync_WhenTrackedReturnExceedsStockOnHand_IsRejectedWithoutMutation()
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
                Code: "SUP-CREDIT-STOCK-001",
                Name: "Supplier Credit Stock",
                Description: "Tracked supplier credit regression",
                Kind: ProductKind.TrackedItem,
                SalePrice: 50m,
                PurchasePrice: 20m,
                SaleTaxTreatment: VatTreatment.Standard,
                PurchaseTaxTreatment: VatTreatment.Standard,
                RevenueAccountId: test.Account("4000").Id,
                ExpenseAccountId: test.Account("5000").Id));

    // Establish inventory accounts and one pre-existing unit.
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
            Reference: "SUP-CREDIT-STOCK-OPENING",
            Note: "Existing stock"));

    var bill =
        await test.Purchasing.PostBillAsync(
            test.UserId,
            new SupplierBillRequest(
                OrganisationId: test.Organisation.Id,
                SupplierId: test.Supplier.Id,
                SupplierReference: "SUP-CREDIT-STOCK-001",
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

    // Three units are now on hand. Consume two so only one remains.
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
            Reference: "SUP-CREDIT-STOCK-CONSUMED",
            Note: "Consume received stock"));

    var beforeCredit =
        await test.Db.ProductItems
            .AsNoTracking()
            .SingleAsync(x => x.Id == item.Id);

    var journalCountBefore =
        await test.Db.PostedJournals.CountAsync();

    var movementCountBefore =
        await test.Db.InventoryMovements.CountAsync();

    var creditCountBefore =
        await test.Db.SupplierCreditNotes.CountAsync();

    var service =
        new SupplierCreditNoteService(
            test.Db,
            test.Access,
            test.Posting);

    var exception =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.CreateAsync(
                    test.UserId,
                    new SupplierCreditNoteRequest(
                        OrganisationId: test.Organisation.Id,
                        SupplierBillId: bill.Id,
                        Date: new DateOnly(2026, 8, 20),
                        Reason: "Return unavailable tracked stock",
                        Amount: bill.Total,
                        ReturnTrackedItems: true)));

    Assert.Contains(
        "Cannot return",
        exception.Message,
        StringComparison.OrdinalIgnoreCase);

    var afterRejectedCredit =
        await test.Db.ProductItems
            .AsNoTracking()
            .SingleAsync(x => x.Id == item.Id);

    Assert.Equal(
        beforeCredit.QuantityOnHand,
        afterRejectedCredit.QuantityOnHand);

    Assert.Equal(
        beforeCredit.AverageCost,
        afterRejectedCredit.AverageCost);

    Assert.Equal(
        journalCountBefore,
        await test.Db.PostedJournals.CountAsync());

    Assert.Equal(
        movementCountBefore,
        await test.Db.InventoryMovements.CountAsync());

    Assert.Equal(
        creditCountBefore,
        await test.Db.SupplierCreditNotes.CountAsync());

    Assert.False(
        await test.Db.InventoryMovements
            .AsNoTracking()
            .AnyAsync(
                x =>
                    x.ProductItemId == item.Id &&
                    x.Type == InventoryMovementType.PurchaseReturn));
}

    [Fact]
    public async Task CreateAsync_WhenAccountsPayableControlAccountHasWrongType_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill = await CreateBillAsync(test);

        var accountsPayable = test.Account("2000");
        accountsPayable.Type = AccountType.Asset;
        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var service =
            new SupplierCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new SupplierCreditNoteRequest(
                            OrganisationId: test.Organisation.Id,
                            SupplierBillId: bill.Id,
                            Date: new DateOnly(2026, 8, 20),
                            Reason: "Invalid AP control type test",
                            Amount: 50m,
                            ReturnTrackedItems: false)));

        Assert.Contains(
            "2000",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());
    }

    private static Task<FijiAccounts.Web.Data.SupplierBill> CreateBillAsync(
        AccountingTestDatabase test) =>
        test.Purchasing.PostBillAsync(
            test.UserId,
            new SupplierBillRequest(
                OrganisationId: test.Organisation.Id,
                SupplierId: test.Supplier.Id,
                SupplierReference: $"SUP-CREDIT-CONTROL-{Guid.NewGuid():N}",
                BillDate: new DateOnly(2026, 8, 18),
                DueDate: new DateOnly(2026, 9, 17),
                Lines:
                [
                    new SupplierBillLineRequest(
                        Description: "Supplier credit control test",
                        Quantity: 1m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        ExpenseAccountId: test.Account("6500").Id)
                ]));
}