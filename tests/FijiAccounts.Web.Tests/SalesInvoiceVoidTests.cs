using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SalesInvoiceVoidTests
{
    [Fact]
    public async Task VoidInvoice_PostsExactReversingJournal()
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
                            Description: "Consulting services",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id)
                    ]));

        var originalJournalId =
            invoice.PostedJournalId!.Value;

        var originalJournal =
            await test.LoadJournalAsync(originalJournalId);

        await test.SalesInvoices.VoidAsync(
            test.UserId,
            test.Organisation.Id,
            invoice.Id,
            new DateOnly(2026, 8, 18));

        var reloadedInvoice =
            await test.Db.SalesInvoices
                .AsNoTracking()
                .SingleAsync(x => x.Id == invoice.Id);

        Assert.Equal(
            InvoiceStatus.Voided,
            reloadedInvoice.Status);

        var journals =
    await test.Db.PostedJournals
        .AsNoTracking()
        .Include(x => x.Lines)
        .Where(x =>
            x.OrganisationId == test.Organisation.Id)
        .ToListAsync();

journals = journals
    .OrderBy(x => x.PostedAt)
    .ToList();

        Assert.Equal(2, journals.Count);

        var reversingJournal =
            journals.Single(
                x => x.Id != originalJournalId);

        Assert.Equal(
            originalJournal.Lines.Sum(x => x.Debit),
            reversingJournal.Lines.Sum(x => x.Credit));

        Assert.Equal(
            originalJournal.Lines.Sum(x => x.Credit),
            reversingJournal.Lines.Sum(x => x.Debit));

        foreach (var originalLine in originalJournal.Lines)
        {
            var reversalLine =
                reversingJournal.Lines.Single(
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
            await test.AccountBalanceAsync("1100"));

        Assert.Equal(
            0m,
            await test.AccountBalanceAsync("4000"));

        Assert.Equal(
            0m,
            await test.AccountBalanceAsync("2100"));
    }

    [Fact]
    public async Task VoidInvoice_WithTrackedSale_RestoresOriginalInventoryPosition()
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
                    Code: "VOID-SALE-STOCK-001",
                    Name: "Tracked sale void item",
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
                Reference: "VOID-SALE-STOCK-OPEN",
                Note: "Opening stock"));

        var beforeSale =
            await test.Db.ProductItems
                .AsNoTracking()
                .SingleAsync(x => x.Id == item.Id);

        Assert.Equal(10m, beforeSale.QuantityOnHand);
        Assert.Equal(20m, beforeSale.AverageCost);

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
        Assert.Equal(20m, afterSale.AverageCost);

        var saleMovement =
            await test.Db.InventoryMovements
                .AsNoTracking()
                .SingleAsync(x =>
                    x.ProductItemId == item.Id &&
                    x.Reference == invoice.InvoiceNumber &&
                    x.QuantityChange < 0);

        Assert.Equal(-2m, saleMovement.QuantityChange);
        Assert.Equal(20m, saleMovement.UnitCost);
        Assert.Equal(-40m, saleMovement.ValueChange);

        await test.SalesInvoices.VoidAsync(
            test.UserId,
            test.Organisation.Id,
            invoice.Id,
            new DateOnly(2026, 8, 19));

        var afterVoid =
            await test.Db.ProductItems
                .AsNoTracking()
                .SingleAsync(x => x.Id == item.Id);

        Assert.Equal(10m, afterVoid.QuantityOnHand);
        Assert.Equal(20m, afterVoid.AverageCost);

        var returnMovement =
            await test.Db.InventoryMovements
                .AsNoTracking()
                .SingleAsync(x =>
                    x.ProductItemId == item.Id &&
                    x.Reference == $"VOID-{invoice.InvoiceNumber}");

        Assert.Equal(
            InventoryMovementType.SalesReturn,
            returnMovement.Type);

        Assert.Equal(2m, returnMovement.QuantityChange);
        Assert.Equal(20m, returnMovement.UnitCost);
        Assert.Equal(40m, returnMovement.ValueChange);

        Assert.Equal(
            saleMovement.PostedJournalId,
            invoice.PostedJournalId);

        Assert.NotEqual(
            saleMovement.PostedJournalId,
            returnMovement.PostedJournalId);

        var reloadedInvoice =
            await test.Db.SalesInvoices
                .AsNoTracking()
                .SingleAsync(x => x.Id == invoice.Id);

        Assert.Equal(
            InvoiceStatus.Voided,
            reloadedInvoice.Status);
    }

    [Fact]
public async Task VoidInvoice_WhenVoidDateIsInsideLockedAccountingPeriod_IsRejectedWithoutMutation()
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
                        Description: "Locked period invoice",
                        Quantity: 1m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        RevenueAccountId: test.Account("4000").Id)
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
        await test.Db.SalesInvoiceVoids.CountAsync();

    var auditCountBefore =
        await test.Db.AuditEvents.CountAsync();

    var exception =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                test.SalesInvoices.VoidAsync(
                    test.UserId,
                    test.Organisation.Id,
                    invoice.Id,
                    new DateOnly(2026, 8, 20)));

    Assert.Contains(
        "locked",
        exception.Message,
        StringComparison.OrdinalIgnoreCase);

    var reloadedInvoice =
        await test.Db.SalesInvoices
            .AsNoTracking()
            .SingleAsync(x => x.Id == invoice.Id);

    Assert.Equal(
        InvoiceStatus.Posted,
        reloadedInvoice.Status);

    Assert.Equal(
        journalCountBefore,
        await test.Db.PostedJournals.CountAsync());

    Assert.Equal(
        voidCountBefore,
        await test.Db.SalesInvoiceVoids.CountAsync());

    Assert.Equal(
        auditCountBefore,
        await test.Db.AuditEvents.CountAsync());
}
}
