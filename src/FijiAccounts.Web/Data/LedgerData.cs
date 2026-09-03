using System.ComponentModel.DataAnnotations;

namespace FijiAccounts.Web.Data;

public sealed class AccountingPeriod
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    [MaxLength(80)] public required string Name { get; set; }
    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }
    public bool IsLocked { get; set; }
    public DateTimeOffset? LockedAt { get; set; }
    public string? LockedByUserId { get; set; }
    public YearEndReview? YearEndReview { get; set; }
    public List<YearEndHandoverPackSnapshot> HandoverPackSnapshots { get; set; } = [];
}

public sealed class YearEndReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Guid AccountingPeriodId { get; set; }
    public AccountingPeriod AccountingPeriod { get; set; } = null!;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public required string StartedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ApprovedByUserId { get; set; }
    [MaxLength(80)] public string? ApprovalReference { get; set; }
    public List<YearEndReviewItem> Items { get; set; } = [];
}

public sealed class YearEndReviewItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid YearEndReviewId { get; set; }
    public YearEndReview YearEndReview { get; set; } = null!;
    public YearEndReviewArea Area { get; set; }
    public YearEndReviewStatus Status { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
    [MaxLength(450)] public string? QueryAssignedToUserId { get; set; }
    public DateOnly? QueryDueDate { get; set; }
    public DateTimeOffset? QueryRaisedAt { get; set; }
    [MaxLength(450)] public string? QueryRaisedByUserId { get; set; }
    [MaxLength(1000)] public string? QueryResponse { get; set; }
    public DateTimeOffset? QueryRespondedAt { get; set; }
    [MaxLength(450)] public string? QueryRespondedByUserId { get; set; }
    public DateTimeOffset? QueryResolvedAt { get; set; }
    [MaxLength(450)] public string? QueryResolvedByUserId { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewedByUserId { get; set; }
    public List<YearEndReviewAttachment> Attachments { get; set; } = [];
}

public sealed class YearEndReviewAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Guid YearEndReviewItemId { get; set; }
    public YearEndReviewItem YearEndReviewItem { get; set; } = null!;
    [MaxLength(255)] public required string FileName { get; set; }
    [MaxLength(100)] public required string ContentType { get; set; }
    public long OriginalSize { get; set; }
    public long StoredSize { get; set; }
    public bool IsCompressed { get; set; }
    public Guid ImmutableDocumentObjectId { get; set; }
    public ImmutableDocumentObject ImmutableDocumentObject { get; set; } = null!;
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string UploadedByUserId { get; set; }
}

public enum YearEndReviewArea
{
    TrialBalance,
    FinancialStatements,
    VatWorkpaper,
    BankReconciliations,
    AgedReceivables,
    AgedPayables,
    FixedAssets,
    InventoryValuation,
    YearEndAdjustments
}

public enum YearEndReviewStatus
{
    Pending,
    Reviewed,
    QueryRaised
}

public sealed class PostedJournal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    public long SequenceNumber { get; set; }
    public DateOnly EntryDate { get; set; }
    [MaxLength(80)] public required string Reference { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    public JournalPurpose Purpose { get; set; }
    public Guid? AdjustmentPeriodId { get; set; }
    public AccountingPeriod? AdjustmentPeriod { get; set; }
    [MaxLength(80)] public string? ApprovalReference { get; set; }
    [MaxLength(3)] public string Currency { get; set; } = "FJD";
    public DateTimeOffset PostedAt { get; set; }
    public required string PostedByUserId { get; set; }
    public List<PostedJournalLine> Lines { get; set; } = [];
}

public enum JournalPurpose
{
    Standard,
    YearEndAdjustment,
    Payroll
}

public sealed class PostedJournalLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PostedJournalId { get; set; }
    public PostedJournal PostedJournal { get; set; } = null!;
    public Guid LedgerAccountId { get; set; }
    public LedgerAccount LedgerAccount { get; set; } = null!;
    public Guid? BranchId { get; set; }
    public Branch? Branch { get; set; }
    public Guid? DivisionId { get; set; }
    public Division? Division { get; set; }
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }
    public Guid? ProjectCostCodeId { get; set; }
    public ProjectCostCode? ProjectCostCode { get; set; }
    [MaxLength(300)] public required string Description { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
}

public sealed class AuditEvent
{
    public long Id { get; set; }
    public Guid OrganisationId { get; set; }
    [MaxLength(80)] public required string EventType { get; set; }
    [MaxLength(80)] public required string EntityType { get; set; }
    [MaxLength(80)] public required string EntityId { get; set; }
    public required string UserId { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public required string JsonData { get; set; }
}
