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
    public decimal ApprovedVariationValue { get; set; }
    public decimal ForecastCost { get; set; }
    public decimal RetentionPercent { get; set; }
    [MaxLength(450)] public required string CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ProjectCostCode> CostCodes { get; set; } = [];

    [NotMapped] public decimal RevisedContractValue =>
        OriginalContractValue + ApprovedVariationValue;
    [NotMapped] public decimal ForecastMargin =>
        RevisedContractValue - ForecastCost;
    [NotMapped] public decimal RetentionExposure =>
        RevisedContractValue * RetentionPercent / 100m;
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
