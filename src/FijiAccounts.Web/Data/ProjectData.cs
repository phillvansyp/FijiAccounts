using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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
    [MaxLength(450)] public required string CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ProjectCostCode> CostCodes { get; set; } = [];
    public List<ProjectVariation> Variations { get; set; } = [];

    [NotMapped] public decimal ApprovedVariationValue =>
        OpeningApprovedVariationValue +
        Variations.Where(x => x.Status == ProjectVariationStatus.Approved).Sum(x => x.Amount);
    [NotMapped] public decimal RevisedContractValue =>
        OriginalContractValue + ApprovedVariationValue;
    [NotMapped] public decimal ForecastMargin =>
        RevisedContractValue - ForecastCost;
    [NotMapped] public decimal RetentionExposure =>
        RevisedContractValue * RetentionPercent / 100m;
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

public sealed class ProjectCostCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    [MaxLength(30)] public required string Code { get; set; }
    [MaxLength(120)] public required string Name { get; set; }
    public decimal BudgetAmount { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
