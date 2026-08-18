using System.ComponentModel.DataAnnotations;

namespace FijiAccounts.Web.Data;

public sealed class FixedAsset
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid OrganisationId { get; set; } public Organisation Organisation { get; set; } = null!;
    [MaxLength(40)] public required string AssetNumber { get; set; } [MaxLength(160)] public required string Name { get; set; }
    public DateOnly AcquisitionDate { get; set; } public decimal Cost { get; set; } public decimal ResidualValue { get; set; } public int UsefulLifeMonths { get; set; }
    public Guid AssetAccountId { get; set; } public LedgerAccount AssetAccount { get; set; } = null!; public Guid DepreciationExpenseAccountId { get; set; } public LedgerAccount DepreciationExpenseAccount { get; set; } = null!; public Guid AccumulatedDepreciationAccountId { get; set; } public LedgerAccount AccumulatedDepreciationAccount { get; set; } = null!;
    public Guid? AcquisitionJournalId { get; set; }
public PostedJournal? AcquisitionJournal { get; set; }

public Guid? AcquisitionBankAccountId { get; set; }
public LedgerAccount? AcquisitionBankAccount { get; set; }
    public bool IsActive { get; set; } = true; public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow; public required string CreatedByUserId { get; set; }
    public List<FixedAssetDepreciation> DepreciationEntries { get; set; } = [];
}

public sealed class FixedAssetDepreciation
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid FixedAssetId { get; set; } public FixedAsset FixedAsset { get; set; } = null!; public DateOnly ThroughDate { get; set; } public decimal Amount { get; set; } public Guid PostedJournalId { get; set; } public PostedJournal PostedJournal { get; set; } = null!; public DateTimeOffset PostedAt { get; set; } = DateTimeOffset.UtcNow; public required string PostedByUserId { get; set; }
}
