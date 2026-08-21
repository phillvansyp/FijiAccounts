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
