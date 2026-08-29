using System.ComponentModel.DataAnnotations;

namespace FijiAccounts.Web.Data;

public enum IntercompanyDocumentType
{
    SalesInvoice,
    SupplierBill,
    CustomerReceipt,
    SupplierPayment,
    Journal
}

public enum IntercompanyMatchStatus
{
    Proposed,
    Confirmed,
    Rejected
}

public sealed class IntercompanyTransactionTag
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationGroupId { get; set; }
    public OrganisationGroup OrganisationGroup { get; set; } = null!;
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    public Guid CounterpartyOrganisationId { get; set; }
    public Organisation CounterpartyOrganisation { get; set; } = null!;
    public IntercompanyDocumentType DocumentType { get; set; }
    public Guid SourceDocumentId { get; set; }
    public DateOnly DocumentDate { get; set; }
    [MaxLength(80)] public required string Reference { get; set; }
    [MaxLength(3)] public required string Currency { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string CreatedByUserId { get; set; }
}

public sealed class IntercompanyTransactionMatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationGroupId { get; set; }
    public OrganisationGroup OrganisationGroup { get; set; } = null!;
    public Guid LeftTransactionTagId { get; set; }
    public IntercompanyTransactionTag LeftTransactionTag { get; set; } = null!;
    public Guid RightTransactionTagId { get; set; }
    public IntercompanyTransactionTag RightTransactionTag { get; set; } = null!;
    public IntercompanyMatchStatus Status { get; set; } = IntercompanyMatchStatus.Proposed;
    public decimal AmountDifference { get; set; }
    public bool HasCurrencyMismatch { get; set; }
    public DateTimeOffset ProposedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string ProposedByUserId { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    [MaxLength(450)] public string? ReviewedByUserId { get; set; }
    public Guid? GroupEliminationJournalId { get; set; }
    public GroupEliminationJournal? GroupEliminationJournal { get; set; }
}
