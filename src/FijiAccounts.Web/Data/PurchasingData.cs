using System.ComponentModel.DataAnnotations;
using FijiAccounts.Domain.Tax;

namespace FijiAccounts.Web.Data;

public enum BillStatus { Posted, PartPaid, Paid, Credited, Voided }

public sealed class SupplierBillDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Guid? SupplierId { get; set; }
    public BusinessParty? Supplier { get; set; }
    [MaxLength(80)] public string SupplierReference { get; set; } = "";
    public DateOnly BillDate { get; set; }
    public DateOnly DueDate { get; set; }
    [MaxLength(300)] public string Description { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public VatTreatment VatTreatment { get; set; } = VatTreatment.Standard;
    public Guid? ExpenseAccountId { get; set; }
    public Guid? ProductItemId { get; set; }
    public string AdditionalLinesJson { get; set; } = "[]";
    [MaxLength(255)] public string? AttachmentFileName { get; set; }
    [MaxLength(100)] public string? AttachmentContentType { get; set; }
    public long? AttachmentOriginalSize { get; set; }
    public bool AttachmentIsCompressed { get; set; }
    public byte[]? AttachmentContent { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string CreatedByUserId { get; set; }
}

    public sealed class SupplierBillVoid
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrganisationId { get; set; }

    public Guid SupplierBillId { get; set; }
    public SupplierBill SupplierBill { get; set; } = null!;

    public DateOnly VoidDate { get; set; }

    [MaxLength(300)]
    public required string Reason { get; set; }

    public Guid PostedJournalId { get; set; }
    public PostedJournal PostedJournal { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; } =
        DateTimeOffset.UtcNow;

    [MaxLength(450)]
    public required string CreatedByUserId { get; set; }
}

public sealed class SupplierBill
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    public Guid SupplierId { get; set; }
    public BusinessParty Supplier { get; set; } = null!;
    public long SequenceNumber { get; set; }
    [MaxLength(40)] public required string BillNumber { get; set; }
    [MaxLength(80)] public required string SupplierReference { get; set; }
    public DateOnly BillDate { get; set; }
    public DateOnly DueDate { get; set; }
    [MaxLength(3)] public string Currency { get; set; } = "FJD";
    public BillStatus Status { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal Total { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal AmountCredited { get; set; }
    public Guid PostedJournalId { get; set; }
    public List<SupplierBillLine> Lines { get; set; } = [];
    public List<SupplierBillAttachment> Attachments { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public required string CreatedByUserId { get; set; }
}

public sealed class SupplierBillAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Guid SupplierBillId { get; set; }
    public SupplierBill SupplierBill { get; set; } = null!;
    [MaxLength(255)] public required string FileName { get; set; }
    [MaxLength(100)] public required string ContentType { get; set; }
    public long OriginalSize { get; set; }
    public long StoredSize { get; set; }
    public bool IsCompressed { get; set; }
    public required byte[] Content { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string UploadedByUserId { get; set; }
}

public sealed class SupplierCreditNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    public Guid SupplierBillId { get; set; }
    public SupplierBill SupplierBill { get; set; } = null!;
    public long SequenceNumber { get; set; }
    [MaxLength(40)] public required string CreditNoteNumber { get; set; }
    public DateOnly CreditDate { get; set; }
    [MaxLength(300)] public required string Reason { get; set; }
    [MaxLength(3)] public string Currency { get; set; } = "FJD";
    public decimal Subtotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal Total { get; set; }
    public bool ReturnedTrackedItems { get; set; }
    public Guid PostedJournalId { get; set; }
    public PostedJournal PostedJournal { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string CreatedByUserId { get; set; }
}

    public sealed class SupplierCreditNoteReversal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }

    public Guid SupplierCreditNoteId { get; set; }
    public SupplierCreditNote SupplierCreditNote { get; set; } = null!;

    public DateOnly ReversalDate { get; set; }

    [MaxLength(300)]
    public required string Reason { get; set; }

    public Guid PostedJournalId { get; set; }
    public PostedJournal PostedJournal { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [MaxLength(450)]
    public required string CreatedByUserId { get; set; }
}

public sealed class SupplierBillLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SupplierBillId { get; set; }
    public SupplierBill SupplierBill { get; set; } = null!;
    [MaxLength(300)] public required string Description { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public VatTreatment VatTreatment { get; set; }
    public decimal VatRate { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal GrossAmount { get; set; }
    public Guid ExpenseAccountId { get; set; }
    public LedgerAccount ExpenseAccount { get; set; } = null!;
    public Guid? ProductItemId { get; set; }
    public ProductItem? ProductItem { get; set; }
}

public sealed class SupplierPayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    public Guid SupplierId { get; set; }
    public BusinessParty Supplier { get; set; } = null!;
    public DateOnly PaymentDate { get; set; }
    [MaxLength(80)] public required string Reference { get; set; }
    public decimal Amount { get; set; }
    public Guid BankAccountId { get; set; }
    public LedgerAccount BankAccount { get; set; } = null!;
    public Guid SupplierBillId { get; set; }
    public SupplierBill SupplierBill { get; set; } = null!;
    public Guid PostedJournalId { get; set; }
    public PostedJournal PostedJournal { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public required string CreatedByUserId { get; set; }
}

public sealed class SupplierPaymentReversal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Guid SupplierPaymentId { get; set; }
    public SupplierPayment SupplierPayment { get; set; } = null!;
    public DateOnly ReversalDate { get; set; }
    [MaxLength(300)] public required string Reason { get; set; }
    public Guid PostedJournalId { get; set; }
    public PostedJournal PostedJournal { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string CreatedByUserId { get; set; }
}

public enum RecurringSupplierBillFrequency
{
    Weekly,
    Monthly,
    Quarterly,
    Yearly
}

public enum RecurringSupplierBillStatus
{
    Active = 0,
    Paused = 1,
    Ended = 2
}
public sealed class RecurringSupplierBill
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }

    public Guid SupplierId { get; set; }
    public BusinessParty Supplier { get; set; } = null!;

    [MaxLength(80)]
    public required string SupplierReference { get; set; }

    public RecurringSupplierBillFrequency Frequency { get; set; }

    public DateOnly StartDate { get; set; }
    public DateOnly NextBillDate { get; set; }

    public int DueDays { get; set; }

    public bool IsActive { get; set; } = true;

    public RecurringSupplierBillStatus Status { get; set; } =
        RecurringSupplierBillStatus.Active;

    public List<RecurringSupplierBillLine> Lines { get; set; } = [];
    public List<RecurringSupplierBillGeneration> Generations { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [MaxLength(450)]
    public required string CreatedByUserId { get; set; }
}

public sealed class RecurringSupplierBillLine
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RecurringSupplierBillId { get; set; }
    public RecurringSupplierBill RecurringSupplierBill { get; set; } = null!;

    [MaxLength(300)]
    public required string Description { get; set; }

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public VatTreatment VatTreatment { get; set; }

    public Guid ExpenseAccountId { get; set; }
    public LedgerAccount ExpenseAccount { get; set; } = null!;

    public Guid? ProductItemId { get; set; }
    public ProductItem? ProductItem { get; set; }
}

public sealed class RecurringSupplierBillGeneration
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrganisationId { get; set; }

    public Guid RecurringSupplierBillId { get; set; }
    public RecurringSupplierBill RecurringSupplierBill { get; set; } = null!;

    public DateOnly ScheduledDate { get; set; }

    public Guid SupplierBillId { get; set; }
    public SupplierBill SupplierBill { get; set; } = null!;

    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    [MaxLength(450)]
    public required string GeneratedByUserId { get; set; }
}

public enum PurchaseOrderStatus
{
    Draft,
    Approved,
    Sent,
    PartiallyReceived,
    Received,
    Closed,
    Cancelled
}

public sealed class PurchaseOrder
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public Guid OrganisationId { get; set; }

    public Organisation Organisation { get; set; } =
        null!;

    public Guid SupplierId { get; set; }

    public BusinessParty Supplier { get; set; } =
        null!;

    public long SequenceNumber { get; set; }

    [MaxLength(40)]
    public required string PurchaseOrderNumber { get; set; }

    public DateOnly OrderDate { get; set; }

    public DateOnly? ExpectedDate { get; set; }

    [MaxLength(80)]
    public string SupplierReference { get; set; } = "";

    [MaxLength(500)]
    public string Notes { get; set; } = "";

    [MaxLength(3)]
    public string Currency { get; set; } = "FJD";

    public PurchaseOrderStatus Status { get; set; } =
        PurchaseOrderStatus.Draft;

    public decimal Subtotal { get; set; }

    public decimal VatTotal { get; set; }

    public decimal Total { get; set; }

    public Guid? SupplierBillDraftId { get; set; }

    public SupplierBillDraft? SupplierBillDraft { get; set; }

    public Guid? SupplierBillId { get; set; }

    public SupplierBill? SupplierBill { get; set; }

    public List<PurchaseOrderLine> Lines { get; set; } =
        [];

    public DateTimeOffset CreatedAt { get; set; } =
        DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } =
        DateTimeOffset.UtcNow;

    [MaxLength(450)]
    public required string CreatedByUserId { get; set; }
}

public sealed class PurchaseOrderLine
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public Guid PurchaseOrderId { get; set; }

    public PurchaseOrder PurchaseOrder { get; set; } =
        null!;

    [MaxLength(300)]
    public required string Description { get; set; }

    public decimal Quantity { get; set; }

    public decimal QuantityReceived { get; set; }

    public decimal UnitPrice { get; set; }

    public VatTreatment VatTreatment { get; set; }

    public decimal VatRate { get; set; }

    public decimal NetAmount { get; set; }

    public decimal VatAmount { get; set; }

    public decimal GrossAmount { get; set; }

    public Guid ExpenseAccountId { get; set; }

    public LedgerAccount ExpenseAccount { get; set; } =
        null!;

    public Guid? ProductItemId { get; set; }

    public ProductItem? ProductItem { get; set; }
}