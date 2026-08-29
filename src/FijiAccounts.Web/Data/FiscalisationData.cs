using System.ComponentModel.DataAnnotations;
using FijiAccounts.Domain.Fiscalisation;

namespace FijiAccounts.Web.Data;

public enum FiscalisationStatus
{
    Prepared,
    Submitting,
    RecoveryRequired,
    Rejected,
    Accepted
}

public enum FiscalSourceDocumentKind
{
    SalesInvoice,
    SalesCreditNote,
    SalesCreditNoteReversal,
    SalesInvoiceVoid
}

public sealed class FiscalisationRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    public FiscalSourceDocumentKind SourceDocumentKind { get; set; }
    public Guid? SalesInvoiceId { get; set; }
    public SalesInvoice? SalesInvoice { get; set; }
    public Guid? SalesCreditNoteId { get; set; }
    public SalesCreditNote? SalesCreditNote { get; set; }
    public Guid? SalesCreditNoteReversalId { get; set; }
    public SalesCreditNoteReversal? SalesCreditNoteReversal { get; set; }
    public Guid? SalesInvoiceVoidId { get; set; }
    public SalesInvoiceVoid? SalesInvoiceVoid { get; set; }
    public FiscalisationStatus Status { get; set; }
    public int AttemptCount { get; set; }
    [MaxLength(64)] public required string RequestHash { get; set; }
    public required string RequestJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string CreatedByUserId { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    [MaxLength(160)] public string? SdcInvoiceNumber { get; set; }
    public DateTimeOffset? SdcIssuedAt { get; set; }
    [MaxLength(2000)] public string? VerificationUrl { get; set; }
    public string? VerificationQrCode { get; set; }
    public string? SignedPayload { get; set; }
    [MaxLength(80)] public string? ErrorCode { get; set; }
    [MaxLength(1000)] public string? ErrorMessage { get; set; }
}

public sealed class FiscalisationConfiguration
{
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    public bool IsEnabled { get; set; }
    public FiscalPaymentType DefaultPaymentType { get; set; } = FiscalPaymentType.Other;
    [MaxLength(80)] public string? StandardTaxLabel { get; set; }
    [MaxLength(80)] public string? ZeroRatedTaxLabel { get; set; }
    [MaxLength(80)] public string? ExemptTaxLabel { get; set; }
    [MaxLength(80)] public string? OutOfScopeTaxLabel { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public string? UpdatedByUserId { get; set; }
}
