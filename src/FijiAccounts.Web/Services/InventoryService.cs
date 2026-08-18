using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record InventoryAdjustmentRequest(Guid OrganisationId, Guid ProductItemId, DateOnly Date, decimal QuantityChange, decimal UnitCost, decimal ReorderLevel, Guid InventoryAccountId, Guid AdjustmentAccountId, string Reference, string? Note);

public sealed class InventoryService(ApplicationDbContext db, TenantAccessService access, JournalPostingService posting)
{
    public async Task<InventoryMovement> AdjustAsync(string userId, InventoryAdjustmentRequest request, CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, request.OrganisationId)) throw new UnauthorizedAccessException("You cannot adjust inventory for this organisation.");
        if (request.QuantityChange == 0 || string.IsNullOrWhiteSpace(request.Reference)) throw new InvalidOperationException("Enter a non-zero quantity and reference.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var item = await db.ProductItems.SingleOrDefaultAsync(x => x.Id == request.ProductItemId && x.OrganisationId == request.OrganisationId && x.IsActive && x.Kind == ProductKind.TrackedItem, ct) ?? throw new InvalidOperationException("Active tracked item not found.");
        if (item.QuantityOnHand + request.QuantityChange < 0) throw new InvalidOperationException("This adjustment would make stock on hand negative.");
        var ids = new[] { request.InventoryAccountId, request.AdjustmentAccountId };
        var accounts = await db.LedgerAccounts.Where(x => x.OrganisationId == request.OrganisationId && x.IsActive && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        if (accounts.Count != 2 || accounts[request.InventoryAccountId].Type != AccountType.Asset || accounts[request.AdjustmentAccountId].Type != AccountType.Expense) throw new InvalidOperationException("Select an active inventory asset and adjustment expense account.");
        var isIncrease = request.QuantityChange > 0;
        var unitCost = isIncrease ? request.UnitCost : item.AverageCost;
        if (unitCost < 0 || (isIncrease && unitCost == 0)) throw new InvalidOperationException("Enter a positive unit cost for stock received.");
        var value = InventoryValuation.MovementValue(Math.Abs(request.QuantityChange), unitCost);
        var journal = await posting.PostAsync(userId, new(request.OrganisationId, request.Date, request.Reference.Trim(), $"Inventory adjustment · {item.Code} · {request.Note}" , isIncrease
            ? [new(request.InventoryAccountId, item.Name, value, 0), new(request.AdjustmentAccountId, item.Name, 0, value)]
            : [new(request.AdjustmentAccountId, item.Name, value, 0), new(request.InventoryAccountId, item.Name, 0, value)]), ct);
        var opening = item.QuantityOnHand == 0 && !await db.InventoryMovements.AnyAsync(x => x.ProductItemId == item.Id, ct);
        if (isIncrease) item.AverageCost = InventoryValuation.WeightedAverage(item.QuantityOnHand, item.AverageCost, request.QuantityChange, unitCost);
        item.QuantityOnHand += request.QuantityChange; item.ReorderLevel = Math.Max(0, request.ReorderLevel); item.InventoryAccountId = request.InventoryAccountId; item.CostAdjustmentAccountId = request.AdjustmentAccountId;
        var movement = new InventoryMovement { OrganisationId = request.OrganisationId, ProductItemId = item.Id, MovementDate = request.Date, Type = opening ? InventoryMovementType.OpeningBalance : isIncrease ? InventoryMovementType.AdjustmentIncrease : InventoryMovementType.AdjustmentDecrease, QuantityChange = request.QuantityChange, UnitCost = unitCost, ValueChange = isIncrease ? value : -value, Reference = request.Reference.Trim(), Note = request.Note?.Trim(), PostedJournalId = journal.Id, PostedByUserId = userId };
        db.InventoryMovements.Add(movement); db.AuditEvents.Add(new AuditEvent { OrganisationId = request.OrganisationId, UserId = userId, EventType = "InventoryAdjusted", EntityType = nameof(ProductItem), EntityId = item.Id.ToString(), JsonData = JsonSerializer.Serialize(new { item.Code, request.QuantityChange, UnitCost = unitCost, ValueChange = movement.ValueChange, item.QuantityOnHand }) });
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return movement;
    }
}
