using System.ComponentModel.DataAnnotations;

namespace FijiAccounts.Web.Data;

public enum OrganisationKind { Business, AccountingPractice }
public enum OrganisationRole { Owner, Administrator, Accountant, Bookkeeper, Payroll, Sales, ReadOnly }
public enum EngagementAccess { ReadOnly, Bookkeeping, Accountant, Full }
public enum OrganisationUnitType { Department, Branch }

public sealed class Organisation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(160)] public required string LegalName { get; set; }
    [MaxLength(80)] public string? TradingName { get; set; }
    [MaxLength(32)] public string? Tin { get; set; }
    [MaxLength(2)] public string CountryCode { get; set; } = "FJ";
    [MaxLength(3)] public string BaseCurrency { get; set; } = "FJD";
    [MaxLength(64)] public string TimeZoneId { get; set; } = "Pacific/Fiji";
    [MaxLength(32)] public string TaxLabel { get; set; } = "VAT";
    public int FinancialYearEndMonth { get; set; } = 12;
    public int FinancialYearEndDay { get; set; } = 31;
    public OrganisationKind Kind { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class OrganisationUnit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    public OrganisationUnitType Type { get; set; }
    [MaxLength(20)] public required string Code { get; set; }
    [MaxLength(120)] public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class OrganisationMembership
{
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    public required string UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public OrganisationRole Role { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AccountantEngagement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PracticeOrganisationId { get; set; }
    public Organisation PracticeOrganisation { get; set; } = null!;
    public Guid ClientOrganisationId { get; set; }
    public Organisation ClientOrganisation { get; set; } = null!;
    public EngagementAccess Access { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
}
