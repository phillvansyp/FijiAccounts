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

public sealed class BankStatementImportDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    public Guid BankAccountId { get; set; }
    public LedgerAccount BankAccount { get; set; } = null!;
    public Guid ImportBatchId { get; set; }
    [MaxLength(255)] public required string FileName { get; set; }
    [MaxLength(100)] public required string ContentType { get; set; }
    public long OriginalSize { get; set; }
    public required byte[] Content { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string UploadedByUserId { get; set; }
}

    public sealed class BankReconciliationSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;

    public Guid BankAccountId { get; set; }
    public LedgerAccount BankAccount { get; set; } = null!;

    public DateOnly StatementStartDate { get; set; }

    public DateOnly StatementEndDate { get; set; }

    public decimal OpeningStatementBalance { get; set; }

    public decimal ClosingStatementBalance { get; set; }

    public decimal LedgerBalance { get; set; }

    public decimal Difference { get; set; }

    public bool IsCompleted { get; set; }

    public DateTimeOffset CreatedAt { get; set; } =
        DateTimeOffset.UtcNow;

    [MaxLength(450)]
    public required string CreatedByUserId { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    [MaxLength(450)]
    public string? CompletedByUserId { get; set; }
}

public sealed class BankTransfer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public Guid DivisionId { get; set; }
    public Division Division { get; set; } = null!;
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

public sealed class BankTransferReversal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }

    public Guid BankTransferId { get; set; }
    public BankTransfer BankTransfer { get; set; } = null!;

    public DateOnly ReversalDate { get; set; }

    [MaxLength(300)]
    public required string Reason { get; set; }

    public Guid PostedJournalId { get; set; }
    public PostedJournal PostedJournal { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; } =
        DateTimeOffset.UtcNow;

    [MaxLength(450)]
    public required string CreatedByUserId { get; set; }
}
