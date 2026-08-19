using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SupplierCreditNoteReversalTests
{
    [Fact]
    public async Task ReverseAsync_RestoresBillAndPostsCompensatingJournal()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill =
            await CreateBillAsync(test);

        var service =
            new SupplierCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var credit =
            await service.CreateAsync(
                test.UserId,
                new SupplierCreditNoteRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierBillId: bill.Id,
                    Date: new DateOnly(2026, 8, 19),
                    Reason: "Test supplier credit",
                    Amount: 50m,
                    ReturnTrackedItems: false));

        var originalJournal =
            await test.Db.PostedJournals
                .AsNoTracking()
                .Include(x => x.Lines)
                .SingleAsync(x => x.Id == credit.PostedJournalId);

        var reversal =
            await service.ReverseAsync(
                test.UserId,
                test.Organisation.Id,
                credit.Id,
                new DateOnly(2026, 8, 20),
                "Reverse supplier credit");

        var reversedJournal =
            await test.Db.PostedJournals
                .AsNoTracking()
                .Include(x => x.Lines)
                .SingleAsync(x => x.Id == reversal.PostedJournalId);

        Assert.Equal(
            originalJournal.Lines.Sum(x => x.Debit),
            reversedJournal.Lines.Sum(x => x.Credit));

        Assert.Equal(
            originalJournal.Lines.Sum(x => x.Credit),
            reversedJournal.Lines.Sum(x => x.Debit));

        var reloaded =
            await test.Db.SupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == bill.Id);

        Assert.Equal(0m, reloaded.AmountCredited);
        Assert.Equal(BillStatus.Posted, reloaded.Status);
    }

    [Fact]
    public async Task ReverseAsync_RejectsSecondReversal()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill =
            await CreateBillAsync(test);

        var service =
            new SupplierCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var credit =
            await service.CreateAsync(
                test.UserId,
                new SupplierCreditNoteRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierBillId: bill.Id,
                    Date: new DateOnly(2026, 8, 19),
                    Reason: "Test supplier credit",
                    Amount: 50m,
                    ReturnTrackedItems: false));

        await service.ReverseAsync(
            test.UserId,
            test.Organisation.Id,
            credit.Id,
            new DateOnly(2026, 8, 20),
            "First reversal");

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.ReverseAsync(
                        test.UserId,
                        test.Organisation.Id,
                        credit.Id,
                        new DateOnly(2026, 8, 21),
                        "Second reversal"));

        Assert.Equal(
            "This supplier credit note has already been reversed.",
            ex.Message);
    }

    [Fact]
    public async Task ReverseAsync_InLockedPeriod_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill =
            await test.Purchasing.PostBillAsync(
                test.UserId,
                new SupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "SUP-CREDIT-LOCK-001",
                    BillDate: new DateOnly(2026, 7, 31),
                    DueDate: new DateOnly(2026, 8, 30),
                    Lines:
                    [
                        new SupplierBillLineRequest(
                            Description: "Supplier credit lock test",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            ExpenseAccountId: test.Account("6500").Id)
                    ]));

        var service =
            new SupplierCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var credit =
            await service.CreateAsync(
                test.UserId,
                new SupplierCreditNoteRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierBillId: bill.Id,
                    Date: new DateOnly(2026, 7, 31),
                    Reason: "Test supplier credit",
                    Amount: 50m,
                    ReturnTrackedItems: false));

        test.Db.AccountingPeriods.Add(
            new AccountingPeriod
            {
                OrganisationId = test.Organisation.Id,
                Name = "August 2026",
                StartsOn = new DateOnly(2026, 8, 1),
                EndsOn = new DateOnly(2026, 8, 31),
                IsLocked = true,
                LockedAt = DateTimeOffset.UtcNow,
                LockedByUserId = test.UserId
            });

        await test.Db.SaveChangesAsync();

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.ReverseAsync(
                        test.UserId,
                        test.Organisation.Id,
                        credit.Id,
                        new DateOnly(2026, 8, 20),
                        "Locked-period reversal"));

        Assert.Equal(
            "The accounting period is locked.",
            ex.Message);

        Assert.Equal(
            0,
            await test.Db.SupplierCreditNoteReversals.CountAsync());

        var reloaded =
            await test.Db.SupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == bill.Id);

        Assert.Equal(50m, reloaded.AmountCredited);
        Assert.Equal(BillStatus.PartPaid, reloaded.Status);
    }

    [Fact]
public async Task ReverseAsync_WhenCreditReturnedTrackedItem_RestoresInventory()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var catalog =
        new ProductCatalogService(
            test.Db,
            test.Access);

    var item =
        await catalog.CreateAsync(
            test.UserId,
            new ProductItemRequest(
                OrganisationId: test.Organisation.Id,
                Code: "SUP-CREDIT-REV-STOCK-001",
                Name: "Supplier credit reversal stock item",
                Description: null,
                Kind: ProductKind.TrackedItem,
                SalePrice: 50m,
                PurchasePrice: 20m,
                SaleTaxTreatment: VatTreatment.Standard,
                PurchaseTaxTreatment: VatTreatment.Standard,
                RevenueAccountId: test.Account("4000").Id,
                ExpenseAccountId: test.Account("5000").Id));

    var inventory =
        new InventoryService(
            test.Db,
            test.Access,
            test.Posting);

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
            Reference: "SUP-CREDIT-REV-OPENING",
            Note: "Existing stock"));

    var bill =
        await test.Purchasing.PostBillAsync(
            test.UserId,
            new SupplierBillRequest(
                OrganisationId: test.Organisation.Id,
                SupplierId: test.Supplier.Id,
                SupplierReference: "SUP-CREDIT-REV-STOCK-BILL",
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

    // Existing 1 @ $30 plus purchased 2 @ $20:
    // weighted average = 70 / 3 = 23.3333.
    Assert.Equal(23.3333m, afterBill.AverageCost);

    var service =
        new SupplierCreditNoteService(
            test.Db,
            test.Access,
            test.Posting);

    var credit =
        await service.CreateAsync(
            test.UserId,
            new SupplierCreditNoteRequest(
                OrganisationId: test.Organisation.Id,
                SupplierBillId: bill.Id,
                Date: new DateOnly(2026, 8, 19),
                Reason: "Return supplier stock",
                Amount: bill.Total,
                ReturnTrackedItems: true));

    var afterCredit =
        await test.Db.ProductItems
            .AsNoTracking()
            .SingleAsync(x => x.Id == item.Id);

    Assert.Equal(1m, afterCredit.QuantityOnHand);
    Assert.Equal(30m, afterCredit.AverageCost);

    var creditMovement =
        await test.Db.InventoryMovements
            .AsNoTracking()
            .SingleAsync(
                x =>
                    x.ProductItemId == item.Id &&
                    x.Reference == credit.CreditNoteNumber);

    Assert.Equal(-2m, creditMovement.QuantityChange);
    Assert.Equal(20m, creditMovement.UnitCost);
    Assert.Equal(-40m, creditMovement.ValueChange);

    var reversal =
        await service.ReverseAsync(
            test.UserId,
            test.Organisation.Id,
            credit.Id,
            new DateOnly(2026, 8, 20),
            "Supplier credit reversed");

    var afterReversal =
        await test.Db.ProductItems
            .AsNoTracking()
            .SingleAsync(x => x.Id == item.Id);

    Assert.Equal(3m, afterReversal.QuantityOnHand);
    Assert.Equal(23.3333m, afterReversal.AverageCost);

    var reversalMovement =
        await test.Db.InventoryMovements
            .AsNoTracking()
            .SingleAsync(
                x =>
                    x.ProductItemId == item.Id &&
                    x.PostedJournalId == reversal.PostedJournalId);

    Assert.Equal(2m, reversalMovement.QuantityChange);
    Assert.Equal(20m, reversalMovement.UnitCost);
    Assert.Equal(40m, reversalMovement.ValueChange);
}

    private static Task<SupplierBill> CreateBillAsync(
        AccountingTestDatabase test) =>
        test.Purchasing.PostBillAsync(
            test.UserId,
            new SupplierBillRequest(
                OrganisationId: test.Organisation.Id,
                SupplierId: test.Supplier.Id,
                SupplierReference: $"SUP-CREDIT-REV-{Guid.NewGuid():N}",
                BillDate: new DateOnly(2026, 8, 18),
                DueDate: new DateOnly(2026, 9, 17),
                Lines:
                [
                    new SupplierBillLineRequest(
                        Description: "Supplier credit reversal helper",
                        Quantity: 1m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        ExpenseAccountId: test.Account("6500").Id)
                ]));
}