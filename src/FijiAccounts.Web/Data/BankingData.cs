using System.ComponentModel.DataAnnotations;

namespace FijiAccounts.Web.Data;

public sealed class BankStatementLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    public Guid BankAccountId { get; set; }
    public LedgerAccount BankAccount { get; set; } = null!;
    public DateOnly TransactionDate { get; set; }
    [MaxLength(300)] public required string Description { get; set; }
    [MaxLength(80)] public string? Reference { get; set; }
    public decimal Amount { get; set; }
    public Guid? MatchedPostedJournalLineId { get; set; }
    public PostedJournalLine? MatchedPostedJournalLine { get; set; }
    public DateTimeOffset? ReconciledAt { get; set; }
    public string? ReconciledByUserId { get; set; }
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(20)] public string Source { get; set; } = "Manual";
    public Guid? ImportBatchId { get; set; }
    [MaxLength(64)] public string? SourceHash { get; set; }
}

public sealed class BankTransfer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Guid FromBankAccountId { get; set; }
    public LedgerAccount FromBankAccount { get; set; } = null!;
    public Guid ToBankAccountId { get; set; }
    public LedgerAccount ToBankAccount { get; set; } = null!;
    public DateOnly TransferDate { get; set; }
    [MaxLength(80)] public required string Reference { get; set; }
    [MaxLength(300)] public string? Description { get; set; }
    public decimal Amount { get; set; }
    public Guid PostedJournalId { get; set; }
    public PostedJournal PostedJournal { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string CreatedByUserId { get; set; }
}
