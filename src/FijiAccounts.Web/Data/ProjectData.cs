using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FijiAccounts.Domain.Tax;

namespace FijiAccounts.Web.Data;

public enum ProjectStatus
{
    Draft,
    Active,
    OnHold,
    Completed,
    Cancelled
}

public enum ProjectVariationStatus
{
    Draft,
    Submitted,
    Approved,
    Rejected,
    Cancelled
}

public enum ProjectProgressClaimStatus
{
    Draft,
    Submitted,
    Approved,
    Rejected,
    Invoiced,
    Cancelled
}

public enum ProjectRevenueRecognitionMethod
{
    CostToCost,
    CertifiedClaims
}

public enum ProjectCostCategory
{
    Other,
    Labour,
    Materials,
    Equipment,
    Subcontractors
}

public sealed class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public Guid DivisionId { get; set; }
    public Division Division { get; set; } = null!;
    public Guid? CustomerId { get; set; }
    public BusinessParty? Customer { get; set; }
    [MaxLength(40)] public required string ProjectNumber { get; set; }
    [MaxLength(160)] public required string Name { get; set; }
    [MaxLength(1000)] public string? Description { get; set; }
    public ProjectStatus Status { get; set; } = ProjectStatus.Draft;
    public DateOnly StartDate { get; set; }
    public DateOnly? ExpectedCompletionDate { get; set; }
    public DateOnly? CompletedDate { get; set; }
    public decimal OriginalContractValue { get; set; }
    public decimal OpeningApprovedVariationValue { get; set; }
    public decimal ForecastCost { get; set; }
    public decimal RetentionPercent { get; set; }
    public ProjectRevenueRecognitionMethod RevenueRecognitionMethod { get; set; } =
        ProjectRevenueRecognitionMethod.CostToCost;
    [MaxLength(450)] public required string CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ProjectCostCode> CostCodes { get; set; } = [];
    public List<ProjectVariation> Variations { get; set; } = [];
    public List<ProjectProgressClaim> ProgressClaims { get; set; } = [];

    [NotMapped] public decimal ApprovedVariationValue =>
        OpeningApprovedVariationValue +
        Variations.Where(x => x.Status == ProjectVariationStatus.Approved).Sum(x => x.Amount);
    [NotMapped] public decimal RevisedContractValue =>
        OriginalContractValue + ApprovedVariationValue;
    [NotMapped] public decimal ForecastMargin =>
        RevisedContractValue - ForecastCost;
    [NotMapped] public decimal RetentionExposure =>
        RevisedContractValue * RetentionPercent / 100m;
    [NotMapped] public decimal CertifiedWorkValue =>
        ProgressClaims.Where(x => x.Status is ProjectProgressClaimStatus.Approved or
            ProjectProgressClaimStatus.Invoiced).Sum(x => x.WorkCompletedAmount);
    [NotMapped] public decimal OutstandingRetention =>
        ProgressClaims.Where(x => x.Status is ProjectProgressClaimStatus.Approved or
            ProjectProgressClaimStatus.Invoiced)
            .Sum(x => x.RetentionHeldAmount - x.RetentionReleasedAmount);
}

public sealed class ProjectVariation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    [MaxLength(40)] public required string VariationNumber { get; set; }
    [MaxLength(160)] public required string Title { get; set; }
    [MaxLength(1000)] public string? Description { get; set; }
    public decimal Amount { get; set; }
    public DateOnly RequestedDate { get; set; }
    public ProjectVariationStatus Status { get; set; } = ProjectVariationStatus.Draft;
    [MaxLength(450)] public required string CreatedByUserId { get; set; }
    [MaxLength(450)] public string? DecidedByUserId { get; set; }
    [MaxLength(500)] public string? DecisionReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}

public sealed class ProjectProgressClaim
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    [MaxLength(40)] public required string ClaimNumber { get; set; }
    [MaxLength(500)] public required string Description { get; set; }
    public DateOnly ClaimPeriodEnd { get; set; }
    public decimal WorkCompletedAmount { get; set; }
    public decimal RetentionRate { get; set; }
    public decimal RetentionHeldAmount { get; set; }
    public decimal RetentionReleasedAmount { get; set; }
    public Guid RevenueAccountId { get; set; }
    public LedgerAccount RevenueAccount { get; set; } = null!;
    public VatTreatment VatTreatment { get; set; }
    public ProjectProgressClaimStatus Status { get; set; } = ProjectProgressClaimStatus.Draft;
    public Guid? SalesInvoiceId { get; set; }
    public SalesInvoice? SalesInvoice { get; set; }
    [MaxLength(450)] public required string CreatedByUserId { get; set; }
    [MaxLength(450)] public string? DecidedByUserId { get; set; }
    [MaxLength(500)] public string? DecisionReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset? InvoicedAt { get; set; }

    [NotMapped] public decimal CertifiedAmount =>
        WorkCompletedAmount - RetentionHeldAmount + RetentionReleasedAmount;
}

public sealed class ProjectCostCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    [MaxLength(30)] public required string Code { get; set; }
    [MaxLength(120)] public required string Name { get; set; }
    public ProjectCostCategory Category { get; set; } = ProjectCostCategory.Other;
    public decimal BudgetAmount { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ProjectWipPosting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public DateOnly AsAt { get; set; }
    public decimal PreviousWipAmount { get; set; }
    public decimal RequiredWipAmount { get; set; }
    public decimal MovementAmount { get; set; }
    public Guid PostedJournalId { get; set; }
    public PostedJournal PostedJournal { get; set; } = null!;
    [MaxLength(450)] public required string PostedByUserId { get; set; }
    public DateTimeOffset PostedAt { get; set; } = DateTimeOffset.UtcNow;
}
