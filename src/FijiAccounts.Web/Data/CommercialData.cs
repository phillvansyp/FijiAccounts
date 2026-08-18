using System.ComponentModel.DataAnnotations;
using FijiAccounts.Domain.Tax;

namespace FijiAccounts.Web.Data;

[Flags] public enum PartyType { Customer = 1, Supplier = 2 }
public enum InvoiceStatus { Draft, Posted, PartPaid, Paid, Voided, Credited }

public sealed class BusinessParty
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    [MaxLength(160)] public required string Name { get; set; }
    [MaxLength(320)] public string? Email { get; set; }
    [MaxLength(40)] public string? Phone { get; set; }
    [MaxLength(32)] public string? Tin { get; set; }
    [MaxLength(500)] public string? Address { get; set; }
    public PartyType Type { get; set; }
    public Guid? DefaultPurchaseAccountId { get; set; }
    public LedgerAccount? DefaultPurchaseAccount { get; set; }
    public VatTreatment? DefaultPurchaseVatTreatment { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class SalesInvoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    public Guid CustomerId { get; set; }
    public BusinessParty Customer { get; set; } = null!;
    public long SequenceNumber { get; set; }
    [MaxLength(40)] public required string InvoiceNumber { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateOnly DueDate { get; set; }
    [MaxLength(3)] public string Currency { get; set; } = "FJD";
    public InvoiceStatus Status { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal Total { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal AmountCredited { get; set; }
    public Guid? PostedJournalId { get; set; }
    public List<SalesInvoiceLine> Lines { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
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
    public Guid PostedJournalId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public required string CreatedByUserId { get; set; }
}

public sealed class SalesInvoiceLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SalesInvoiceId { get; set; }
    public SalesInvoice SalesInvoice { get; set; } = null!;
    [MaxLength(300)] public required string Description { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public VatTreatment VatTreatment { get; set; }
    public decimal VatRate { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal GrossAmount { get; set; }
    public Guid RevenueAccountId { get; set; }
    public LedgerAccount RevenueAccount { get; set; } = null!;
    public Guid? ProductItemId { get; set; }
    public ProductItem? ProductItem { get; set; }
}

public sealed class CustomerReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    public Guid CustomerId { get; set; }
    public BusinessParty Customer { get; set; } = null!;
    public DateOnly ReceiptDate { get; set; }
    [MaxLength(80)] public required string Reference { get; set; }
    public decimal Amount { get; set; }
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
