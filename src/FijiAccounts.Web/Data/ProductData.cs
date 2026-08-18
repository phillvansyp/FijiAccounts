using System.ComponentModel.DataAnnotations;
using FijiAccounts.Domain.Tax;

namespace FijiAccounts.Web.Data;

public enum ProductKind { Service, NonTrackedItem, TrackedItem }
public sealed class ProductItem
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid OrganisationId { get; set; } public Organisation Organisation { get; set; } = null!;
    [MaxLength(40)] public required string Code { get; set; } [MaxLength(160)] public required string Name { get; set; } [MaxLength(500)] public string? Description { get; set; } public ProductKind Kind { get; set; }
    public decimal SalePrice { get; set; } public decimal PurchasePrice { get; set; } public VatTreatment SaleTaxTreatment { get; set; } = VatTreatment.Standard; public VatTreatment PurchaseTaxTreatment { get; set; } = VatTreatment.Standard;
    public Guid? RevenueAccountId { get; set; } public LedgerAccount? RevenueAccount { get; set; } public Guid? ExpenseAccountId { get; set; } public LedgerAccount? ExpenseAccount { get; set; } public bool IsActive { get; set; } = true; public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public decimal QuantityOnHand { get; set; }
    public decimal AverageCost { get; set; }
    public decimal ReorderLevel { get; set; }
    public Guid? InventoryAccountId { get; set; }
    public LedgerAccount? InventoryAccount { get; set; }
    public Guid? CostAdjustmentAccountId { get; set; }
    public LedgerAccount? CostAdjustmentAccount { get; set; }
}

public enum InventoryMovementType { OpeningBalance, AdjustmentIncrease, AdjustmentDecrease, SalesReturn, PurchaseReturn }
public sealed class InventoryMovement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Guid ProductItemId { get; set; }
    public ProductItem ProductItem { get; set; } = null!;
    public DateOnly MovementDate { get; set; }
    public InventoryMovementType Type { get; set; }
    public decimal QuantityChange { get; set; }
    public decimal UnitCost { get; set; }
    public decimal ValueChange { get; set; }
    [MaxLength(80)] public required string Reference { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
    public Guid PostedJournalId { get; set; }
    public PostedJournal PostedJournal { get; set; } = null!;
    [MaxLength(450)] public required string PostedByUserId { get; set; }
    public DateTimeOffset PostedAt { get; set; } = DateTimeOffset.UtcNow;
}
