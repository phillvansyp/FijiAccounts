using System.ComponentModel.DataAnnotations;

namespace FijiAccounts.Web.Data;

public enum BankRuleDirection { Any, MoneyIn, MoneyOut }
public sealed class BankRule
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid OrganisationId { get; set; } public Organisation Organisation { get; set; } = null!; [MaxLength(120)] public required string Name { get; set; } [MaxLength(160)] public required string DescriptionContains { get; set; } public BankRuleDirection Direction { get; set; } public Guid TargetAccountId { get; set; } public LedgerAccount TargetAccount { get; set; } = null!; public bool IsActive { get; set; } = true; public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow; public required string CreatedByUserId { get; set; }
}
