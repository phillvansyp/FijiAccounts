using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class ProductCatalogAccountingTests
{
    [Fact]
    public async Task CreateAsync_NormalizesCodeAndPersistsItem()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new ProductCatalogService(
                test.Db,
                test.Access);

        var item =
            await service.CreateAsync(
                test.UserId,
                new ProductItemRequest(
                    OrganisationId: test.Organisation.Id,
                    Code: "  svc-001  ",
                    Name: "  Consulting Service  ",
                    Description: "  Monthly consulting  ",
                    Kind: ProductKind.Service,
                    SalePrice: 100m,
                    PurchasePrice: 0m,
                    SaleTaxTreatment: VatTreatment.Standard,
                    PurchaseTaxTreatment: VatTreatment.Standard,
                    RevenueAccountId: test.Account("4000").Id,
                    ExpenseAccountId: null));

        Assert.Equal("SVC-001", item.Code);
        Assert.Equal("Consulting Service", item.Name);
        Assert.Equal("Monthly consulting", item.Description);
        Assert.Equal(100m, item.SalePrice);
        Assert.True(item.IsActive);

        var stored =
            await test.Db.ProductItems
                .AsNoTracking()
                .SingleAsync(x => x.Id == item.Id);

        Assert.Equal("SVC-001", stored.Code);
    }

    [Fact]
    public async Task CreateAsync_WhenCodeAlreadyExists_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new ProductCatalogService(
                test.Db,
                test.Access);

        var request =
            new ProductItemRequest(
                OrganisationId: test.Organisation.Id,
                Code: "DUP-001",
                Name: "Duplicate Product",
                Description: null,
                Kind: ProductKind.Service,
                SalePrice: 10m,
                PurchasePrice: 0m,
                SaleTaxTreatment: VatTreatment.Standard,
                PurchaseTaxTreatment: VatTreatment.Standard,
                RevenueAccountId: test.Account("4000").Id,
                ExpenseAccountId: null);

        await service.CreateAsync(
            test.UserId,
            request);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        request));

        Assert.Contains(
            "already exists",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_WhenPriceIsNegative_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new ProductCatalogService(
                test.Db,
                test.Access);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new ProductItemRequest(
                            OrganisationId: test.Organisation.Id,
                            Code: "NEG-001",
                            Name: "Negative Price",
                            Description: null,
                            Kind: ProductKind.Service,
                            SalePrice: -1m,
                            PurchasePrice: 0m,
                            SaleTaxTreatment: VatTreatment.Standard,
                            PurchaseTaxTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id,
                            ExpenseAccountId: null)));

        Assert.Contains(
            "non-negative",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_WhenRevenueAccountHasWrongType_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new ProductCatalogService(
                test.Db,
                test.Access);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new ProductItemRequest(
                            OrganisationId: test.Organisation.Id,
                            Code: "REV-WRONG-001",
                            Name: "Wrong Revenue",
                            Description: null,
                            Kind: ProductKind.Service,
                            SalePrice: 10m,
                            PurchasePrice: 0m,
                            SaleTaxTreatment: VatTreatment.Standard,
                            PurchaseTaxTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("6500").Id,
                            ExpenseAccountId: null)));

        Assert.Contains(
            "active revenue account",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_WhenExpenseAccountHasWrongType_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new ProductCatalogService(
                test.Db,
                test.Access);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new ProductItemRequest(
                            OrganisationId: test.Organisation.Id,
                            Code: "EXP-WRONG-001",
                            Name: "Wrong Expense",
                            Description: null,
                            Kind: ProductKind.TrackedItem,
                            SalePrice: 20m,
                            PurchasePrice: 10m,
                            SaleTaxTreatment: VatTreatment.Standard,
                            PurchaseTaxTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id,
                            ExpenseAccountId: test.Account("4000").Id)));

        Assert.Contains(
            "expense or asset account",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SetActiveAsync_ArchivesAndReactivatesItem()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new ProductCatalogService(
                test.Db,
                test.Access);

        var item =
            await service.CreateAsync(
                test.UserId,
                new ProductItemRequest(
                    OrganisationId: test.Organisation.Id,
                    Code: "ACTIVE-001",
                    Name: "Active Toggle",
                    Description: null,
                    Kind: ProductKind.Service,
                    SalePrice: 10m,
                    PurchasePrice: 0m,
                    SaleTaxTreatment: VatTreatment.Standard,
                    PurchaseTaxTreatment: VatTreatment.Standard,
                    RevenueAccountId: test.Account("4000").Id,
                    ExpenseAccountId: null));

        await service.SetActiveAsync(
            test.UserId,
            test.Organisation.Id,
            item.Id,
            false);

        var archived =
            await test.Db.ProductItems
                .AsNoTracking()
                .SingleAsync(x => x.Id == item.Id);

        Assert.False(archived.IsActive);

        await service.SetActiveAsync(
            test.UserId,
            test.Organisation.Id,
            item.Id,
            true);

        var reactivated =
            await test.Db.ProductItems
                .AsNoTracking()
                .SingleAsync(x => x.Id == item.Id);

        Assert.True(reactivated.IsActive);
    }

    [Fact]
    public async Task CreateAndSetActiveAsync_WriteAuditEvents()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new ProductCatalogService(
                test.Db,
                test.Access);

        var item =
            await service.CreateAsync(
                test.UserId,
                new ProductItemRequest(
                    OrganisationId: test.Organisation.Id,
                    Code: "AUDIT-PROD-001",
                    Name: "Audit Product",
                    Description: null,
                    Kind: ProductKind.Service,
                    SalePrice: 10m,
                    PurchasePrice: 0m,
                    SaleTaxTreatment: VatTreatment.Standard,
                    PurchaseTaxTreatment: VatTreatment.Standard,
                    RevenueAccountId: test.Account("4000").Id,
                    ExpenseAccountId: null));

        await service.SetActiveAsync(
            test.UserId,
            test.Organisation.Id,
            item.Id,
            false);

        var events =
            await test.Db.AuditEvents
                .AsNoTracking()
                .Where(x =>
                    x.EntityType == nameof(ProductItem) &&
                    x.EntityId == item.Id.ToString())
                .ToListAsync();

        Assert.Contains(
            events,
            x => x.EventType == "ProductItemCreated");

        Assert.Contains(
            events,
            x => x.EventType == "ProductItemArchived");
    }
}