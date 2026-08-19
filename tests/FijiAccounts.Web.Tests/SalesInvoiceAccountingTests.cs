using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SalesInvoiceAccountingTests
{
    [Fact]
    public async Task PostInvoice_WithFijiVat_PostsBalancedArRevenueAndVatJournal()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var result =
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
                            Description: "Consulting services",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id)
                    ]));

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.NotNull(result.PostedJournalId);

        Assert.Equal(100m, result.Subtotal);
        Assert.Equal(12.50m, result.VatTotal);
        Assert.Equal(112.50m, result.Total);

        var journal =
            await test.LoadJournalAsync(
                result.PostedJournalId!.Value);

        var totalDebit =
            journal.Lines.Sum(x => x.Debit);

        var totalCredit =
            journal.Lines.Sum(x => x.Credit);

        Assert.Equal(totalDebit, totalCredit);

        Assert.Equal(112.50m, totalDebit);
        Assert.Equal(112.50m, totalCredit);

        var receivables =
            journal.Lines.Single(
                x => x.LedgerAccount.Code == "1100");

        var sales =
            journal.Lines.Single(
                x => x.LedgerAccount.Code == "4000");

        var vatPayable =
            journal.Lines.Single(
                x => x.LedgerAccount.Code == "2100");

        Assert.Equal(112.50m, receivables.Debit);
        Assert.Equal(0m, receivables.Credit);

        Assert.Equal(0m, sales.Debit);
        Assert.Equal(100m, sales.Credit);

        Assert.Equal(0m, vatPayable.Debit);
        Assert.Equal(12.50m, vatPayable.Credit);
    }

    [Fact]
    public async Task CreateAndPostAsync_WhenReceivablesControlAccountIsInactive_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var receivables = test.Account("1100");
        receivables.IsActive = false;

        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
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
                                    Description: "Inactive AR control test",
                                    Quantity: 1m,
                                    UnitPrice: 100m,
                                    VatTreatment: VatTreatment.Standard,
                                    RevenueAccountId: test.Account("4000").Id)
                            ])));

        Assert.Contains(
            "1100",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());
    }

    [Fact]
    public async Task CreateAndPostAsync_WhenReceivablesControlAccountHasWrongType_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var receivables = test.Account("1100");
        receivables.Type = AccountType.Liability;

        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
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
                                    Description: "Invalid AR control type test",
                                    Quantity: 1m,
                                    UnitPrice: 100m,
                                    VatTreatment: VatTreatment.Standard,
                                    RevenueAccountId: test.Account("4000").Id)
                            ])));

        Assert.Contains(
            "1100",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());
    }

    [Fact]
    public async Task CreateAndPostAsync_WhenVatPayableControlAccountIsInactive_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var vatPayable = test.Account("2100");
        vatPayable.IsActive = false;

        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
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
                                    Description: "Inactive VAT control test",
                                    Quantity: 1m,
                                    UnitPrice: 100m,
                                    VatTreatment: VatTreatment.Standard,
                                    RevenueAccountId: test.Account("4000").Id)
                            ])));

        Assert.Contains(
            "2100",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());
    }

    [Fact]
    public async Task CreateAndPostAsync_WhenVatPayableControlAccountHasWrongType_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var vatPayable = test.Account("2100");
        vatPayable.Type = AccountType.Asset;

        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
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
                                    Description: "Invalid VAT control type test",
                                    Quantity: 1m,
                                    UnitPrice: 100m,
                                    VatTreatment: VatTreatment.Standard,
                                    RevenueAccountId: test.Account("4000").Id)
                            ])));

        Assert.Contains(
            "2100",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());
    }

    [Fact]
public async Task CreateAndPostAsync_WhenTrackedItemInventoryAccountHasWrongType_IsRejected()
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
                Code: "SALE-STOCK-CONTROL-001",
                Name: "Tracked sale control item",
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
            Reference: "SALE-STOCK-CONTROL-OPEN",
            Note: "Opening stock"));

    var trackedItem =
        await test.Db.ProductItems
            .SingleAsync(x => x.Id == item.Id);

    trackedItem.InventoryAccountId =
        test.Account("4000").Id;

    await test.Db.SaveChangesAsync();

    var journalCountBefore =
        await test.Db.PostedJournals.CountAsync();

    var invoiceCountBefore =
        await test.Db.SalesInvoices.CountAsync();

    var movementCountBefore =
        await test.Db.InventoryMovements
            .CountAsync(x => x.ProductItemId == item.Id);

    var quantityBefore =
        trackedItem.QuantityOnHand;

    var ex =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
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
                                Description: "Tracked item sale",
                                Quantity: 2m,
                                UnitPrice: 100m,
                                VatTreatment: VatTreatment.Standard,
                                RevenueAccountId: test.Account("4000").Id,
                                ProductItemId: item.Id)
                        ])));

    Assert.Contains(
        "inventory",
        ex.Message,
        StringComparison.OrdinalIgnoreCase);

    Assert.Equal(
        journalCountBefore,
        await test.Db.PostedJournals.CountAsync());

    Assert.Equal(
        invoiceCountBefore,
        await test.Db.SalesInvoices.CountAsync());

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

    [Fact]
public async Task CreateAndPostAsync_WhenTrackedItemCostAdjustmentAccountHasWrongType_IsRejected()
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
                Code: "SALE-STOCK-COST-CONTROL-001",
                Name: "Tracked sale cost control item",
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
            Reference: "SALE-STOCK-COST-CONTROL-OPEN",
            Note: "Opening stock"));

    var trackedItem =
        await test.Db.ProductItems
            .SingleAsync(x => x.Id == item.Id);

    trackedItem.CostAdjustmentAccountId =
        test.Account("4000").Id;

    await test.Db.SaveChangesAsync();

    var journalCountBefore =
        await test.Db.PostedJournals.CountAsync();

    var invoiceCountBefore =
        await test.Db.SalesInvoices.CountAsync();

    var movementCountBefore =
        await test.Db.InventoryMovements
            .CountAsync(x => x.ProductItemId == item.Id);

    var quantityBefore =
        trackedItem.QuantityOnHand;

    var ex =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
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
                                Description: "Tracked item sale",
                                Quantity: 2m,
                                UnitPrice: 100m,
                                VatTreatment: VatTreatment.Standard,
                                RevenueAccountId: test.Account("4000").Id,
                                ProductItemId: item.Id)
                        ])));

    Assert.Contains(
        "cost",
        ex.Message,
        StringComparison.OrdinalIgnoreCase);

    Assert.Equal(
        journalCountBefore,
        await test.Db.PostedJournals.CountAsync());

    Assert.Equal(
        invoiceCountBefore,
        await test.Db.SalesInvoices.CountAsync());

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