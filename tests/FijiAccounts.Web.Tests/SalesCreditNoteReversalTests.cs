using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SalesCreditNoteReversalTests
{
    [Fact]
    public async Task ReverseAsync_RestoresInvoiceAndPostsCompensatingJournal()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice =
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
                            Description: "Sales credit reversal test",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id)
                    ]));

        var service =
            new SalesCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var credit =
            await service.CreateAsync(
                test.UserId,
                new SalesCreditNoteRequest(
                    OrganisationId: test.Organisation.Id,
                    SalesInvoiceId: invoice.Id,
                    Date: new DateOnly(2026, 8, 19),
                    Reason: "Test credit",
                    Amount: 50m,
                    RestockTrackedItems: false));

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
                "Reverse test credit");

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
            await test.Db.SalesInvoices
                .AsNoTracking()
                .SingleAsync(x => x.Id == invoice.Id);

        Assert.Equal(0m, reloaded.AmountCredited);
        Assert.Equal(InvoiceStatus.Posted, reloaded.Status);
    }

    [Fact]
    public async Task ReverseAsync_RejectsSecondReversal()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice =
            await CreateInvoiceAsync(test);

        var service =
            new SalesCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var credit =
            await service.CreateAsync(
                test.UserId,
                new SalesCreditNoteRequest(
                    OrganisationId: test.Organisation.Id,
                    SalesInvoiceId: invoice.Id,
                    Date: new DateOnly(2026, 8, 19),
                    Reason: "Test credit",
                    Amount: 50m,
                    RestockTrackedItems: false));

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
            "This sales credit note has already been reversed.",
            ex.Message);
    }

    [Fact]
    public async Task ReverseAsync_InLockedPeriod_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice =
            await CreateInvoiceAsync(test);

        var service =
            new SalesCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var credit =
            await service.CreateAsync(
                test.UserId,
                new SalesCreditNoteRequest(
                    OrganisationId: test.Organisation.Id,
                    SalesInvoiceId: invoice.Id,
                    Date: new DateOnly(2026, 7, 31),
                    Reason: "Test credit",
                    Amount: 50m,
                    RestockTrackedItems: false));

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
            await test.Db.SalesCreditNoteReversals.CountAsync());

        var reloaded =
            await test.Db.SalesInvoices
                .AsNoTracking()
                .SingleAsync(x => x.Id == invoice.Id);

        Assert.Equal(50m, reloaded.AmountCredited);
        Assert.Equal(InvoiceStatus.PartPaid, reloaded.Status);
    }

        [Fact]
    public async Task ReverseAsync_WhenCreditRestockedTrackedItem_RemovesRestockedInventory()
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
                    Code: "CREDIT-REV-STOCK-001",
                    Name: "Credit reversal stock item",
                    Description: null,
                    Kind: ProductKind.TrackedItem,
                    SalePrice: 100m,
                    PurchasePrice: 20m,
                    SaleTaxTreatment: VatTreatment.Standard,
                    PurchaseTaxTreatment: VatTreatment.Standard,
                    RevenueAccountId: test.Account("4000").Id,
                    ExpenseAccountId: test.Account("5000").Id));

        await inventory.AdjustAsync(
            test.UserId,
            new InventoryAdjustmentRequest(
                OrganisationId: test.Organisation.Id,
                ProductItemId: item.Id,
                Date: new DateOnly(2026, 8, 17),
                QuantityChange: 10m,
                UnitCost: 20m,
                ReorderLevel: 0m,
                InventoryAccountId: test.Account("1200").Id,
                AdjustmentAccountId: test.Account("5000").Id,
                Reference: "CREDIT-REV-OPENING",
                Note: "Opening stock"));

        var invoice =
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
                            Description: "Tracked item sale",
                            Quantity: 2m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id,
                            ProductItemId: item.Id)
                    ]));

        var afterSale =
            await test.Db.ProductItems
                .AsNoTracking()
                .SingleAsync(x => x.Id == item.Id);

        Assert.Equal(8m, afterSale.QuantityOnHand);

        var service =
            new SalesCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var credit =
            await service.CreateAsync(
                test.UserId,
                new SalesCreditNoteRequest(
                    OrganisationId: test.Organisation.Id,
                    SalesInvoiceId: invoice.Id,
                    Date: new DateOnly(2026, 8, 19),
                    Reason: "Return tracked sale",
                    Amount: invoice.Total,
                    RestockTrackedItems: true));

        var afterCredit =
            await test.Db.ProductItems
                .AsNoTracking()
                .SingleAsync(x => x.Id == item.Id);

        Assert.Equal(10m, afterCredit.QuantityOnHand);

        var creditMovement =
            await test.Db.InventoryMovements
                .AsNoTracking()
                .SingleAsync(
                    x =>
                        x.ProductItemId == item.Id &&
                        x.Reference == credit.CreditNoteNumber);

        Assert.Equal(2m, creditMovement.QuantityChange);
        Assert.Equal(20m, creditMovement.UnitCost);
        Assert.Equal(40m, creditMovement.ValueChange);

        var reversal =
            await service.ReverseAsync(
                test.UserId,
                test.Organisation.Id,
                credit.Id,
                new DateOnly(2026, 8, 20),
                "Customer kept returned stock");

        var afterReversal =
            await test.Db.ProductItems
                .AsNoTracking()
                .SingleAsync(x => x.Id == item.Id);

        Assert.Equal(8m, afterReversal.QuantityOnHand);

        var reversalMovement =
            await test.Db.InventoryMovements
                .AsNoTracking()
                .SingleAsync(
                    x =>
                        x.ProductItemId == item.Id &&
                        x.PostedJournalId == reversal.PostedJournalId);

        Assert.Equal(-2m, reversalMovement.QuantityChange);
        Assert.Equal(20m, reversalMovement.UnitCost);
        Assert.Equal(-40m, reversalMovement.ValueChange);
    }

    [Fact]
public async Task ReverseAsync_WhenRestockedUnitsHaveBeenConsumed_IsRejected()
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
                Code: "CREDIT-REV-CONSUMED-001",
                Name: "Consumed return stock item",
                Description: null,
                Kind: ProductKind.TrackedItem,
                SalePrice: 100m,
                PurchasePrice: 20m,
                SaleTaxTreatment: VatTreatment.Standard,
                PurchaseTaxTreatment: VatTreatment.Standard,
                RevenueAccountId: test.Account("4000").Id,
                ExpenseAccountId: test.Account("5000").Id));

    await inventory.AdjustAsync(
        test.UserId,
        new InventoryAdjustmentRequest(
            OrganisationId: test.Organisation.Id,
            ProductItemId: item.Id,
            Date: new DateOnly(2026, 8, 16),
            QuantityChange: 2m,
            UnitCost: 20m,
            ReorderLevel: 0m,
            InventoryAccountId: test.Account("1200").Id,
            AdjustmentAccountId: test.Account("5000").Id,
            Reference: "CREDIT-REV-CONSUMED-OPEN",
            Note: "Opening stock"));

    var invoice =
        await test.SalesInvoices.CreateAndPostAsync(
            test.UserId,
            new SalesInvoiceRequest(
                OrganisationId: test.Organisation.Id,
                CustomerId: test.Customer.Id,
                IssueDate: new DateOnly(2026, 8, 17),
                DueDate: new DateOnly(2026, 9, 16),
                Lines:
                [
                    new SalesInvoiceLineRequest(
                        Description: "Tracked sale to credit",
                        Quantity: 2m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        RevenueAccountId: test.Account("4000").Id,
                        ProductItemId: item.Id)
                ]));

    var service =
        new SalesCreditNoteService(
            test.Db,
            test.Access,
            test.Posting);

    var credit =
        await service.CreateAsync(
            test.UserId,
            new SalesCreditNoteRequest(
                OrganisationId: test.Organisation.Id,
                SalesInvoiceId: invoice.Id,
                Date: new DateOnly(2026, 8, 18),
                Reason: "Return full sale",
                Amount: invoice.Total,
                RestockTrackedItems: true));

    var afterCredit =
        await test.Db.ProductItems
            .AsNoTracking()
            .SingleAsync(x => x.Id == item.Id);

    Assert.Equal(2m, afterCredit.QuantityOnHand);

    // Consume one of the two units restored by the credit.
    await inventory.AdjustAsync(
        test.UserId,
        new InventoryAdjustmentRequest(
            OrganisationId: test.Organisation.Id,
            ProductItemId: item.Id,
            Date: new DateOnly(2026, 8, 19),
            QuantityChange: -1m,
            UnitCost: 999m,
            ReorderLevel: 0m,
            InventoryAccountId: test.Account("1200").Id,
            AdjustmentAccountId: test.Account("5000").Id,
            Reference: "CONSUME-RETURNED-STOCK",
            Note: "Consume one returned unit"));

    var beforeReversal =
        await test.Db.ProductItems
            .AsNoTracking()
            .SingleAsync(x => x.Id == item.Id);

    Assert.Equal(1m, beforeReversal.QuantityOnHand);

    var journalCountBefore =
        await test.Db.PostedJournals.CountAsync();

    var reversalCountBefore =
        await test.Db.SalesCreditNoteReversals.CountAsync();

    var ex =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.ReverseAsync(
                    test.UserId,
                    test.Organisation.Id,
                    credit.Id,
                    new DateOnly(2026, 8, 20),
                    "Attempt reversal after stock consumed"));

    Assert.Contains(
        "no longer has all returned units on hand",
        ex.Message,
        StringComparison.OrdinalIgnoreCase);

    Assert.Equal(
        journalCountBefore,
        await test.Db.PostedJournals.CountAsync());

    Assert.Equal(
        reversalCountBefore,
        await test.Db.SalesCreditNoteReversals.CountAsync());

    var afterRejected =
        await test.Db.ProductItems
            .AsNoTracking()
            .SingleAsync(x => x.Id == item.Id);

    Assert.Equal(1m, afterRejected.QuantityOnHand);

    var reloadedInvoice =
        await test.Db.SalesInvoices
            .AsNoTracking()
            .SingleAsync(x => x.Id == invoice.Id);

    Assert.Equal(invoice.Total, reloadedInvoice.AmountCredited);
    Assert.Equal(InvoiceStatus.Credited, reloadedInvoice.Status);
}

    private static Task<SalesInvoice> CreateInvoiceAsync(
        AccountingTestDatabase test) =>
        test.SalesInvoices.CreateAndPostAsync(
            test.UserId,
            new SalesInvoiceRequest(
                OrganisationId: test.Organisation.Id,
                CustomerId: test.Customer.Id,
                IssueDate: new DateOnly(2026, 8, 18),
                DueDate: new DateOnly(2026, 9, 17),
                Lines:
                [
                    new SalesInvoiceLineRequest(
                        Description: "Sales credit reversal helper",
                        Quantity: 1m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        RevenueAccountId: test.Account("4000").Id)
                ]));
}