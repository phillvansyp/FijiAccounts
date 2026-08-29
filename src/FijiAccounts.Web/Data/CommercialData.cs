using System.ComponentModel.DataAnnotations;
using FijiAccounts.Domain.Tax;

namespace FijiAccounts.Web.Data;

[Flags] public enum PartyType { Customer = 1, Supplier = 2 }
public enum InvoiceStatus { Draft, Posted, PartPaid, Paid, Voided, Credited }
public enum SalesCreditNoteStatus { Posted = 0, Draft = 1 }

public sealed class BusinessParty
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    [MaxLength(160)] public required string Name { get; set; }
    [MaxLength(320)] public string? Email { get; set; }
    [MaxLength(320)] public string? AccountsEmail { get; set; }
    [MaxLength(40)] public string? Phone { get; set; }
    [MaxLength(32)] public string? Tin { get; set; }
    [MaxLength(80)] public string? VatRegistrationNumber { get; set; }
    [MaxLength(500)] public string? Address { get; set; }
    public PartyType Type { get; set; }
    public List<BusinessPartyDocument> Documents { get; set; } = [];
    public List<SupplierAccountProfile> SupplierAccounts { get; set; } = [];
    public List<SupplierBankAccount> SupplierBankAccounts { get; set; } = [];
    public Guid? DefaultSalesAccountId { get; set; }
    public LedgerAccount? DefaultSalesAccount { get; set; }
    public VatTreatment? DefaultSalesVatTreatment { get; set; }
    public Guid? DefaultPurchaseAccountId { get; set; }
    public LedgerAccount? DefaultPurchaseAccount { get; set; }
    public VatTreatment? DefaultPurchaseVatTreatment { get; set; }
    [MaxLength(3)] public string? DefaultSalesCurrency { get; set; }
    [MaxLength(3)] public string? DefaultPurchaseCurrency { get; set; }

    public PaymentTermType DefaultSalesInvoicePaymentTermType { get; set; } =
        PaymentTermType.DaysAfterDocumentDate;

    [Range(0, 365)]
    public int DefaultSalesInvoiceDueDays { get; set; } = 30;

    public PaymentTermType DefaultSupplierBillPaymentTermType { get; set; } =
        PaymentTermType.DaysAfterDocumentDate;

    [Range(0, 365)]
    public int DefaultSupplierBillDueDays { get; set; } = 30;

    public bool IsActive { get; set; } = true;
}

public sealed class SupplierAccountProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    public Guid SupplierId { get; set; }
    public BusinessParty Supplier { get; set; } = null!;
    [MaxLength(80)] public required string Label { get; set; }
    [MaxLength(80)] public required string AccountNumber { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class SupplierBankAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    public Guid SupplierId { get; set; }
    public BusinessParty Supplier { get; set; } = null!;
    [MaxLength(120)] public required string AccountName { get; set; }
    [MaxLength(120)] public string? BankName { get; set; }
    [MaxLength(80)] public required string AccountNumber { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    [MaxLength(450)] public required string SubmittedByUserId { get; set; }
    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public string? VerifiedByUserId { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public bool IsVerified => VerifiedAt is not null;
}

public enum BusinessPartyDocumentType
{
    Contract,
    Agreement,
    Insurance,
    Certificate,
    Compliance,
    Pricing,
    Other
}

public sealed class BusinessPartyDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;

    public Guid BusinessPartyId { get; set; }
    public BusinessParty BusinessParty { get; set; } = null!;

    public BusinessPartyDocumentType Type { get; set; }

    [MaxLength(200)]
    public required string Name { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(255)]
    public required string FileName { get; set; }

    [MaxLength(100)]
    public required string ContentType { get; set; }

    public long OriginalSize { get; set; }

    public long StoredSize { get; set; }

    public bool IsCompressed { get; set; }

    public required byte[] Content { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    public DateTimeOffset UploadedAtUtc { get; set; }
        = DateTimeOffset.UtcNow;

    [MaxLength(450)]
    public required string UploadedByUserId { get; set; }
}
public enum RecurringSalesInvoiceFrequency
{
    Weekly,
    Monthly,
    Quarterly,
    Yearly
}

public enum RecurringSalesInvoiceStatus
{
    Active = 0,
    Paused = 1,
    Ended = 2
}

public sealed class RecurringSalesInvoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Guid? BranchId { get; set; }
    public Branch? Branch { get; set; }
    public Guid? DivisionId { get; set; }
    public Division? Division { get; set; }

    public Guid CustomerId { get; set; }
    public BusinessParty Customer { get; set; } = null!;

    public RecurringSalesInvoiceFrequency Frequency { get; set; }

    public DateOnly StartDate { get; set; }
    public DateOnly NextInvoiceDate { get; set; }

    public int DueDays { get; set; }
    [MaxLength(3)] public string Currency { get; set; } = "FJD";

    public bool IsActive { get; set; } = true;

    public RecurringSalesInvoiceStatus Status { get; set; } =
        RecurringSalesInvoiceStatus.Active;

    public List<RecurringSalesInvoiceLine> Lines { get; set; } = [];
    public List<RecurringSalesInvoiceGeneration> Generations { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [MaxLength(450)]
    public required string CreatedByUserId { get; set; }
}

public sealed class RecurringSalesInvoiceLine
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RecurringSalesInvoiceId { get; set; }
    public RecurringSalesInvoice RecurringSalesInvoice { get; set; } = null!;

    [MaxLength(300)]
    public required string Description { get; set; }

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public VatTreatment VatTreatment { get; set; }

    public Guid RevenueAccountId { get; set; }
    public LedgerAccount RevenueAccount { get; set; } = null!;

    public Guid? ProductItemId { get; set; }
    public ProductItem? ProductItem { get; set; }

    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public Guid? ProjectCostCodeId { get; set; }
    public ProjectCostCode? ProjectCostCode { get; set; }
}

public sealed class RecurringSalesInvoiceGeneration
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrganisationId { get; set; }

    public Guid RecurringSalesInvoiceId { get; set; }
    public RecurringSalesInvoice RecurringSalesInvoice { get; set; } = null!;

    public DateOnly ScheduledDate { get; set; }

    public Guid SalesInvoiceId { get; set; }
    public SalesInvoice SalesInvoice { get; set; } = null!;

    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    [MaxLength(450)]
    public required string GeneratedByUserId { get; set; }
}


public sealed class RecurringInvoiceAutomationRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrganisationId { get; set; }

    public DateOnly RunDate { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public int GeneratedCount { get; set; }

    [MaxLength(32)]
    public required string Status { get; set; }

    [MaxLength(1000)]
    public string? ErrorMessage { get; set; }
}

public sealed class SalesInvoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    public Guid? BranchId { get; set; }
    public Branch? Branch { get; set; }
    public Guid? DivisionId { get; set; }
    public Division? Division { get; set; }
    public Guid CustomerId { get; set; }
    public BusinessParty Customer { get; set; } = null!;
    public long SequenceNumber { get; set; }
    [MaxLength(40)] public required string InvoiceNumber { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateOnly DueDate { get; set; }
    [MaxLength(3)] public string Currency { get; set; } = "FJD";
    public decimal ExchangeRateToBase { get; set; } = 1m;
    public decimal TransactionSubtotal { get; set; }
    public decimal TransactionVatTotal { get; set; }
    public decimal TransactionTotal { get; set; }
    public InvoiceStatus Status { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal Total { get; set; }
    public decimal TransactionAmountPaid { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal AmountCredited { get; set; }
    public bool? IsTaxInvoice { get; set; }
    public bool IsSimplifiedTaxInvoice { get; set; }
    [MaxLength(80)] public string? TaxDocumentComplianceVersion { get; set; }
    [MaxLength(160)] public string? SupplierNameSnapshot { get; set; }
    [MaxLength(500)] public string? SupplierAddressSnapshot { get; set; }
    [MaxLength(32)] public string? SupplierTinSnapshot { get; set; }
    [MaxLength(160)] public string? RecipientNameSnapshot { get; set; }
    [MaxLength(500)] public string? RecipientAddressSnapshot { get; set; }
    [MaxLength(32)] public string? RecipientTinSnapshot { get; set; }
    public Guid? PostedJournalId { get; set; }
    public List<SalesInvoiceLine> Lines { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public required string CreatedByUserId { get; set; }
}

public enum SalesInvoiceVoidStatus
{
    Posted = 0,
    Draft = 1
}

public sealed class SalesInvoiceVoid
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrganisationId { get; set; }

    public Guid SalesInvoiceId { get; set; }
    public SalesInvoice SalesInvoice { get; set; } = null!;

    public DateOnly VoidDate { get; set; }

    public SalesInvoiceVoidStatus Status { get; set; } = SalesInvoiceVoidStatus.Posted;

    public Guid? PostedJournalId { get; set; }
    public PostedJournal? PostedJournal { get; set; }

    public DateTimeOffset CreatedAt { get; set; } =
        DateTimeOffset.UtcNow;

    [MaxLength(450)]
    public required string CreatedByUserId { get; set; }
}

public sealed class SalesCreditNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    public Guid SalesInvoiceId { get; set; }
    public SalesInvoice SalesInvoice { get; set; } = null!;
    public long SequenceNumber { get; set; }
    [MaxLength(40)] public required string CreditNoteNumber { get; set; }
    public DateOnly CreditDate { get; set; }
    [MaxLength(300)] public required string Reason { get; set; }
    [MaxLength(3)] public string Currency { get; set; } = "FJD";
    public decimal Subtotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal Total { get; set; }
    public decimal OriginalInvoiceVatAmount { get; set; }
    public decimal AdjustedInvoiceVatAmount { get; set; }
    public SalesCreditNoteStatus Status { get; set; } = SalesCreditNoteStatus.Posted;
    public bool RestockTrackedItems { get; set; }
    public Guid? PostedJournalId { get; set; }
    public List<SalesCreditNoteLine> Lines { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public required string CreatedByUserId { get; set; }
}

public sealed class SalesCreditNoteLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SalesCreditNoteId { get; set; }
    public SalesCreditNote SalesCreditNote { get; set; } = null!;
    public Guid SalesInvoiceLineId { get; set; }
    public SalesInvoiceLine SalesInvoiceLine { get; set; } = null!;
    [MaxLength(300)] public required string Description { get; set; }
    public VatTreatment VatTreatment { get; set; }
    public decimal VatRate { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal GrossAmount { get; set; }
    public Guid RevenueAccountId { get; set; }
    public Guid? ProductItemId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? ProjectCostCodeId { get; set; }
}

public enum SalesCreditNoteReversalStatus { Posted = 0, Draft = 1 }

public sealed class SalesCreditNoteReversal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }

    public Guid SalesCreditNoteId { get; set; }
    public SalesCreditNote SalesCreditNote { get; set; } = null!;

    public DateOnly ReversalDate { get; set; }

    [MaxLength(300)]
    public required string Reason { get; set; }

    public SalesCreditNoteReversalStatus Status { get; set; } = SalesCreditNoteReversalStatus.Posted;
    public Guid? PostedJournalId { get; set; }
    public PostedJournal? PostedJournal { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [MaxLength(450)]
    public required string CreatedByUserId { get; set; }
}

public sealed class SalesInvoiceLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SalesInvoiceId { get; set; }
    public SalesInvoice SalesInvoice { get; set; } = null!;
    [MaxLength(300)] public required string Description { get; set; }

    [MaxLength(80)]
    public string? CustomerPurchaseOrderNumber { get; set; }

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TransactionUnitPrice { get; set; }
    public VatTreatment VatTreatment { get; set; }
    public decimal VatRate { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal TransactionNetAmount { get; set; }
    public decimal TransactionVatAmount { get; set; }
    public decimal TransactionGrossAmount { get; set; }
    public Guid RevenueAccountId { get; set; }
    public LedgerAccount RevenueAccount { get; set; } = null!;
    public Guid? ProductItemId { get; set; }
    public ProductItem? ProductItem { get; set; }
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }
    public Guid? ProjectCostCodeId { get; set; }
    public ProjectCostCode? ProjectCostCode { get; set; }
}

public sealed class CustomerReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    public Guid? BranchId { get; set; }
    public Branch? Branch { get; set; }
    public Guid? DivisionId { get; set; }
    public Division? Division { get; set; }
    public Guid CustomerId { get; set; }
    public BusinessParty Customer { get; set; } = null!;
    public DateOnly ReceiptDate { get; set; }
    [MaxLength(80)] public required string Reference { get; set; }
    public decimal Amount { get; set; }
    [MaxLength(3)] public string Currency { get; set; } = "FJD";
    public decimal TransactionAmount { get; set; }
    public decimal ExchangeRateToBase { get; set; } = 1m;
    public decimal RealisedExchangeDifference { get; set; }
    public Guid BankAccountId { get; set; }
    public LedgerAccount BankAccount { get; set; } = null!;
    public Guid PostedJournalId { get; set; }
    public PostedJournal PostedJournal { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public required string CreatedByUserId { get; set; }
    public List<CustomerReceiptAllocation> Allocations { get; set; } = [];
}

public sealed class CustomerReceiptAllocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerReceiptId { get; set; }
    public CustomerReceipt CustomerReceipt { get; set; } = null!;
    public Guid SalesInvoiceId { get; set; }
    public SalesInvoice SalesInvoice { get; set; } = null!;
    public decimal TransactionAmount { get; set; }
    public decimal Amount { get; set; }
}

public sealed class CustomerReceiptReversal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Guid CustomerReceiptId { get; set; }
    public CustomerReceipt CustomerReceipt { get; set; } = null!;
    public DateOnly ReversalDate { get; set; }
    [MaxLength(300)] public required string Reason { get; set; }
    public Guid PostedJournalId { get; set; }
    public PostedJournal PostedJournal { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string CreatedByUserId { get; set; }
}
