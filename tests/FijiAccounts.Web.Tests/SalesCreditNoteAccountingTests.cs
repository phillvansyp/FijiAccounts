using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SalesCreditNoteAccountingTests
{
    [Fact]
    public async Task CreateAsync_WhenReceivablesControlAccountIsInactive_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice = await CreateInvoiceAsync(test);

        var receivables = test.Account("1100");
        receivables.IsActive = false;
        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var service =
            new SalesCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new SalesCreditNoteRequest(
                            OrganisationId: test.Organisation.Id,
                            SalesInvoiceId: invoice.Id,
                            Date: new DateOnly(2026, 8, 20),
                            Reason: "Inactive receivables control test",
                            Amount: 50m,
                            RestockTrackedItems: false)));

        Assert.Contains(
            "1100",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_WhenReceivablesControlAccountHasWrongType_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice = await CreateInvoiceAsync(test);

        var receivables = test.Account("1100");
        receivables.Type = AccountType.Liability;
        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var service =
            new SalesCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new SalesCreditNoteRequest(
                            OrganisationId: test.Organisation.Id,
                            SalesInvoiceId: invoice.Id,
                            Date: new DateOnly(2026, 8, 20),
                            Reason: "Invalid receivables control type test",
                            Amount: 50m,
                            RestockTrackedItems: false)));

        Assert.Contains(
            "1100",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_WhenVatPayableControlAccountIsInactive_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice = await CreateInvoiceAsync(test);

        var vatPayable = test.Account("2100");
        vatPayable.IsActive = false;
        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var service =
            new SalesCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new SalesCreditNoteRequest(
                            OrganisationId: test.Organisation.Id,
                            SalesInvoiceId: invoice.Id,
                            Date: new DateOnly(2026, 8, 20),
                            Reason: "Inactive VAT payable control test",
                            Amount: 50m,
                            RestockTrackedItems: false)));

        Assert.Contains(
            "2100",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_WhenVatPayableControlAccountHasWrongType_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice = await CreateInvoiceAsync(test);

        var vatPayable = test.Account("2100");
        vatPayable.Type = AccountType.Asset;
        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var service =
            new SalesCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new SalesCreditNoteRequest(
                            OrganisationId: test.Organisation.Id,
                            SalesInvoiceId: invoice.Id,
                            Date: new DateOnly(2026, 8, 20),
                            Reason: "Invalid VAT payable control type test",
                            Amount: 50m,
                            RestockTrackedItems: false)));

        Assert.Contains(
            "2100",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());
    }

    [Fact]
public async Task CreateAsync_WhenTrackedItemInventoryAccountHasWrongType_IsRejected()
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
                Code: "CREDIT-STOCK-CONTROL-001",
                Name: "Tracked credit control item",
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
            Reference: "CREDIT-STOCK-CONTROL-OPEN",
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
                        Description: "Tracked credit sale",
                        Quantity: 2m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        RevenueAccountId: test.Account("4000").Id,
                        ProductItemId: item.Id)
                ]));

    var trackedItem =
        await test.Db.ProductItems
            .SingleAsync(x => x.Id == item.Id);

    trackedItem.InventoryAccountId =
        test.Account("4000").Id;

    await test.Db.SaveChangesAsync();

    var journalCountBefore =
        await test.Db.PostedJournals.CountAsync();

    var creditCountBefore =
        await test.Db.SalesCreditNotes.CountAsync();

    var movementCountBefore =
        await test.Db.InventoryMovements
            .CountAsync(x => x.ProductItemId == item.Id);

    var quantityBefore =
        trackedItem.QuantityOnHand;

    var service =
        new SalesCreditNoteService(
            test.Db,
            test.Access,
            test.Posting);

    var ex =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.CreateAsync(
                    test.UserId,
                    new SalesCreditNoteRequest(
                        OrganisationId: test.Organisation.Id,
                        SalesInvoiceId: invoice.Id,
                        Date: new DateOnly(2026, 8, 19),
                        Reason: "Tracked inventory drift test",
                        Amount: invoice.Total,
                        RestockTrackedItems: true)));

    Assert.Contains(
        "inventory",
        ex.Message,
        StringComparison.OrdinalIgnoreCase);

    Assert.Equal(
        journalCountBefore,
        await test.Db.PostedJournals.CountAsync());

    Assert.Equal(
        creditCountBefore,
        await test.Db.SalesCreditNotes.CountAsync());

    Assert.Equal(
        movementCountBefore,
        await test.Db.InventoryMovements
            .CountAsync(x => x.ProductItemId == item.Id));

    var reloaded =
        await test.Db.ProductItems
            .AsNoTracking()
            .SingleAsync(x => x.Id == item.Id);

    Assert.Equal(quantityBefore, reloaded.QuantityOnHand);
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
                        Description: "Sales credit control test",
                        Quantity: 1m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        RevenueAccountId: test.Account("4000").Id)
                ]));

    [Fact]
public async Task CreateAsync_WhenTrackedItemCostAdjustmentAccountHasWrongType_IsRejected()
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
                Code: "CREDIT-STOCK-COST-CONTROL-001",
                Name: "Tracked credit cost control item",
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
            Reference: "CREDIT-STOCK-COST-CONTROL-OPEN",
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
                        Description: "Tracked credit sale",
                        Quantity: 2m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        RevenueAccountId: test.Account("4000").Id,
                        ProductItemId: item.Id)
                ]));

    var trackedItem =
        await test.Db.ProductItems
            .SingleAsync(x => x.Id == item.Id);

    trackedItem.CostAdjustmentAccountId =
        test.Account("4000").Id;

    await test.Db.SaveChangesAsync();

    var journalCountBefore =
        await test.Db.PostedJournals.CountAsync();

    var creditCountBefore =
        await test.Db.SalesCreditNotes.CountAsync();

    var movementCountBefore =
        await test.Db.InventoryMovements
            .CountAsync(x => x.ProductItemId == item.Id);

    var quantityBefore =
        trackedItem.QuantityOnHand;

    var service =
        new SalesCreditNoteService(
            test.Db,
            test.Access,
            test.Posting);

    var ex =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.CreateAsync(
                    test.UserId,
                    new SalesCreditNoteRequest(
                        OrganisationId: test.Organisation.Id,
                        SalesInvoiceId: invoice.Id,
                        Date: new DateOnly(2026, 8, 19),
                        Reason: "Tracked cost drift test",
                        Amount: invoice.Total,
                        RestockTrackedItems: true)));

    Assert.Contains(
        "cost",
        ex.Message,
        StringComparison.OrdinalIgnoreCase);

    Assert.Equal(
        journalCountBefore,
        await test.Db.PostedJournals.CountAsync());

    Assert.Equal(
        creditCountBefore,
        await test.Db.SalesCreditNotes.CountAsync());

    Assert.Equal(
        movementCountBefore,
        await test.Db.InventoryMovements
            .CountAsync(x => x.ProductItemId == item.Id));

    var reloaded =
        await test.Db.ProductItems
            .AsNoTracking()
            .SingleAsync(x => x.Id == item.Id);

    Assert.Equal(quantityBefore, reloaded.QuantityOnHand);
}
}