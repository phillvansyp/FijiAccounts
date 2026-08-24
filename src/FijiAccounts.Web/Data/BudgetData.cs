using System.ComponentModel.DataAnnotations;

namespace FijiAccounts.Web.Data;

public sealed class AccountBudget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    public Guid LedgerAccountId { get; set; }
    public LedgerAccount LedgerAccount { get; set; } = null!;
    [MaxLength(80)] public string ScopeKey { get; set; } = "organisation";
    public Guid? BranchId { get; set; }
    public Branch? Branch { get; set; }
    public Guid? DivisionId { get; set; }
    public Division? Division { get; set; }
    public DateOnly Month { get; set; }
    public decimal Amount { get; set; }
    [MaxLength(450)] public required string UpdatedByUserId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
