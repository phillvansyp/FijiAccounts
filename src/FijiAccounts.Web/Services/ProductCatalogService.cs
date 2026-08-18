using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record ProductItemRequest(Guid OrganisationId, string Code, string Name, string? Description, ProductKind Kind, decimal SalePrice, decimal PurchasePrice, VatTreatment SaleTaxTreatment, VatTreatment PurchaseTaxTreatment, Guid? RevenueAccountId, Guid? ExpenseAccountId);
public sealed class ProductCatalogService(ApplicationDbContext db, TenantAccessService access)
{
    public async Task<ProductItem> CreateAsync(string userId, ProductItemRequest request, CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, request.OrganisationId)) throw new UnauthorizedAccessException("You cannot maintain products for this organisation."); var code = request.Code.Trim().ToUpperInvariant(); if (code.Length is < 1 or > 40 || string.IsNullOrWhiteSpace(request.Name) || request.SalePrice < 0 || request.PurchasePrice < 0) throw new InvalidOperationException("Enter a valid item code, name and non-negative prices.");
        if (await db.ProductItems.AnyAsync(x => x.OrganisationId == request.OrganisationId && x.Code == code, ct)) throw new InvalidOperationException($"Item code {code} already exists.");
        if (request.RevenueAccountId is not null && !await db.LedgerAccounts.AnyAsync(x => x.Id == request.RevenueAccountId && x.OrganisationId == request.OrganisationId && x.IsActive && x.Type == AccountType.Revenue, ct)) throw new InvalidOperationException("Select an active revenue account."); if (request.ExpenseAccountId is not null && !await db.LedgerAccounts.AnyAsync(x => x.Id == request.ExpenseAccountId && x.OrganisationId == request.OrganisationId && x.IsActive && (x.Type == AccountType.Expense || x.Type == AccountType.Asset), ct)) throw new InvalidOperationException("Select an active expense or asset account.");
        var item = new ProductItem { OrganisationId = request.OrganisationId, Code = code, Name = request.Name.Trim(), Description = request.Description?.Trim(), Kind = request.Kind, SalePrice = request.SalePrice, PurchasePrice = request.PurchasePrice, SaleTaxTreatment = request.SaleTaxTreatment, PurchaseTaxTreatment = request.PurchaseTaxTreatment, RevenueAccountId = request.RevenueAccountId, ExpenseAccountId = request.ExpenseAccountId }; db.ProductItems.Add(item); db.AuditEvents.Add(Audit(request.OrganisationId, userId, "ProductItemCreated", item.Id, new { item.Code, item.Name, item.Kind })); await db.SaveChangesAsync(ct); return item;
    }
    public async Task SetActiveAsync(string userId, Guid organisationId, Guid itemId, bool active, CancellationToken ct = default) { if (!await access.CanPostJournalsAsync(userId, organisationId)) throw new UnauthorizedAccessException("You cannot maintain products for this organisation."); var item = await db.ProductItems.SingleOrDefaultAsync(x => x.Id == itemId && x.OrganisationId == organisationId, ct) ?? throw new InvalidOperationException("Product or service not found."); item.IsActive = active; db.AuditEvents.Add(Audit(organisationId, userId, active ? "ProductItemReactivated" : "ProductItemArchived", item.Id, new { item.Code })); await db.SaveChangesAsync(ct); }
    private static AuditEvent Audit(Guid organisationId, string userId, string eventType, Guid id, object data) => new() { OrganisationId = organisationId, UserId = userId, EventType = eventType, EntityType = nameof(ProductItem), EntityId = id.ToString(), JsonData = JsonSerializer.Serialize(data) };
}
