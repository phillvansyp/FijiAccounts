using System.ComponentModel.DataAnnotations;
using FijiAccounts.Domain.Accounting;

namespace FijiAccounts.Web.Data;

public enum GroupAccountPurpose
{
    Standard,
    IntercompanyReceivable,
    IntercompanyPayable,
    IntercompanyRevenue,
    IntercompanyExpense
}

public sealed class GroupLedgerAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationGroupId { get; set; }
    public OrganisationGroup OrganisationGroup { get; set; } = null!;
    [MaxLength(32)] public required string Code { get; set; }
    [MaxLength(160)] public required string Name { get; set; }
    public AccountType Type { get; set; }
    public GroupAccountPurpose Purpose { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string CreatedByUserId { get; set; }
    public List<GroupLedgerAccountMapping> Mappings { get; set; } = [];
}

public sealed class GroupLedgerAccountMapping
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationGroupId { get; set; }
    public OrganisationGroup OrganisationGroup { get; set; } = null!;
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    public Guid LedgerAccountId { get; set; }
    public LedgerAccount LedgerAccount { get; set; } = null!;
    public Guid GroupLedgerAccountId { get; set; }
    public GroupLedgerAccount GroupLedgerAccount { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string CreatedByUserId { get; set; }
}

public sealed class IntercompanyAccountConfiguration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationGroupId { get; set; }
    public OrganisationGroup OrganisationGroup { get; set; } = null!;
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    public Guid CounterpartyOrganisationId { get; set; }
    public Organisation CounterpartyOrganisation { get; set; } = null!;
    public Guid ReceivableAccountId { get; set; }
    public Guid PayableAccountId { get; set; }
    public Guid RevenueAccountId { get; set; }
    public Guid ExpenseAccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string UpdatedByUserId { get; set; }
}
