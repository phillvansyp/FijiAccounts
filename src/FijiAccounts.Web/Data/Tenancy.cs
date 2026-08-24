using System.ComponentModel.DataAnnotations;

namespace FijiAccounts.Web.Data;

public enum OrganisationKind { Business, AccountingPractice }
public enum OrganisationRole { Owner, Administrator, Accountant, Bookkeeper, Payroll, Sales, ReadOnly }
public enum DimensionAccessMode { All, Restricted }
public enum OrganisationGroupRole { Owner, Administrator, Viewer }
public enum TenantStatus { Active, Suspended, Archived }
public enum GroupExchangeRateType { PeriodAverage, Closing }
public enum EngagementAccess { ReadOnly, Bookkeeping, Accountant, Full }
public enum OrganisationUnitType { Department, Branch }

public sealed class OrganisationGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(160)]
    public required string Name { get; set; }

    [MaxLength(3)]
    public string PresentationCurrency { get; set; } = "FJD";

    public List<Organisation> Companies { get; set; } = [];
    public List<OrganisationGroupMembership> Memberships { get; set; } = [];
    public List<GroupExchangeRate> ExchangeRates { get; set; } = [];

    public bool IsDemo { get; set; }
    public TenantStatus Status { get; set; } = TenantStatus.Active;
    public DateTimeOffset? SuspendedAt { get; set; }

    [MaxLength(1000)]
    public string? InternalNotes { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PlatformAuditEvent
{
    public long Id { get; set; }
    [MaxLength(450)] public required string AdministratorUserId { get; set; }
    [MaxLength(80)] public required string EventType { get; set; }
    public Guid? OrganisationGroupId { get; set; }
    public Guid? OrganisationId { get; set; }
    [MaxLength(500)] public required string Reason { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public required string JsonData { get; set; }
}

public sealed class GroupExchangeRate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationGroupId { get; set; }
    public OrganisationGroup OrganisationGroup { get; set; } = null!;

    [MaxLength(3)]
    public required string FromCurrency { get; set; }

    [MaxLength(3)]
    public required string ToCurrency { get; set; }

    public GroupExchangeRateType Type { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public decimal Rate { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class OrganisationGroupMembership
{
    public Guid OrganisationGroupId { get; set; }
    public OrganisationGroup OrganisationGroup { get; set; } = null!;
    public required string UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public OrganisationGroupRole Role { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Organisation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? OrganisationGroupId { get; set; }
    public OrganisationGroup? OrganisationGroup { get; set; }
    [MaxLength(160)] public required string LegalName { get; set; }
    [MaxLength(80)] public string? TradingName { get; set; }
    [MaxLength(32)] public string? Tin { get; set; }
    [MaxLength(2)] public string CountryCode { get; set; } = "FJ";
    [MaxLength(3)] public string BaseCurrency { get; set; } = "FJD";
    [MaxLength(64)] public string TimeZoneId { get; set; } = "Pacific/Fiji";

    [MaxLength(20)] public string SalesInvoicePrefix { get; set; } = "INV-";
    public long NextSalesInvoiceNumber { get; set; } = 1;

    [MaxLength(20)] public string SalesQuotePrefix { get; set; } = "QU-";
    public long NextSalesQuoteNumber { get; set; } = 1;

    [MaxLength(20)] public string SalesCreditNotePrefix { get; set; } = "CN-";
    public long NextSalesCreditNoteNumber { get; set; } = 1;

    [MaxLength(20)] public string PurchaseOrderPrefix { get; set; } = "PO-";
    public long NextPurchaseOrderNumber { get; set; } = 1;

    [MaxLength(20)] public string SupplierBillPrefix { get; set; } = "BILL-";
    public long NextSupplierBillNumber { get; set; } = 1;

    [MaxLength(20)] public string SupplierCreditNotePrefix { get; set; } = "SCN-";
    public long NextSupplierCreditNoteNumber { get; set; } = 1;

    public bool RecurringInvoiceAutomationEnabled { get; set; } = true;

    public TimeOnly RecurringInvoiceAutomationTime { get; set; } =
        new(6, 0);

    public PaymentTermType DefaultSalesInvoicePaymentTermType { get; set; } =
        PaymentTermType.DaysAfterDocumentDate;

    [Range(0, 365)]
    public int DefaultSalesInvoiceDueDays { get; set; } = 30;

    public PaymentTermType DefaultSupplierBillPaymentTermType { get; set; } =
        PaymentTermType.DaysAfterDocumentDate;

    [Range(0, 365)]
    public int DefaultSupplierBillDueDays { get; set; } = 30;

    [Range(0, 100)]
    public decimal PurchaseQuantityTolerancePercent { get; set; }

    [Range(0, 100)]
    public decimal PurchasePriceTolerancePercent { get; set; } = 2m;

    [Range(0, 1000000)]
    public decimal PurchaseTotalToleranceAmount { get; set; } = 5m;

    [MaxLength(32)] public string TaxLabel { get; set; } = "VAT";
    public int FinancialYearEndMonth { get; set; } = 12;
    public int FinancialYearEndDay { get; set; } = 31;
    public DateOnly? ConversionDate { get; set; }
    public OrganisationKind Kind { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Branch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;

    [MaxLength(20)]
    public required string Code { get; set; }

    [MaxLength(120)]
    public required string Name { get; set; }

    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public List<Division> Divisions { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Division
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    [MaxLength(20)]
    public required string Code { get; set; }

    [MaxLength(120)]
    public required string Name { get; set; }

    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
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
    public DimensionAccessMode DimensionAccessMode { get; set; } = DimensionAccessMode.All;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class OrganisationDimensionAccessGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public required string UserId { get; set; }
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public Guid? DivisionId { get; set; }
    public Division? Division { get; set; }
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
