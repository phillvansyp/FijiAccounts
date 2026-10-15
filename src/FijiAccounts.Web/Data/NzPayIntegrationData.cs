using System.ComponentModel.DataAnnotations;

namespace FijiAccounts.Web.Data;

public enum NzPayFbtJournalStatus
{
    AwaitingApproval,
    Posted,
    Rejected
}

/// <summary>A one-way credential issued by Account Island for one NZ organisation.</summary>
public sealed class NzPayApiCredential
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    [MaxLength(80)] public string Name { get; set; } = "NZ Pay";
    [MaxLength(64)] public required string TokenSha256 { get; set; }
    [MaxLength(20)] public required string TokenPrefix { get; set; }
    [MaxLength(200)] public string Scopes { get; set; } = "fixed-assets.read fbt-journals.create";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string CreatedByUserId { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    [MaxLength(450)] public string? RevokedByUserId { get; set; }
}

/// <summary>An NZ Pay FBT journal awaiting normal Account Island approval.</summary>
public sealed class NzPayFbtJournalDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    public Guid CredentialId { get; set; }
    public NzPayApiCredential Credential { get; set; } = null!;
    [MaxLength(120)] public required string ExternalJournalId { get; set; }
    [MaxLength(160)] public required string IdempotencyKey { get; set; }
    [MaxLength(64)] public required string PayloadSha256 { get; set; }
    public DateOnly EntryDate { get; set; }
    [MaxLength(80)] public required string Reference { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    [MaxLength(3)] public string Currency { get; set; } = "NZD";
    public decimal Amount { get; set; }
    public string AssetReferencesJson { get; set; } = "[]";
    public NzPayFbtJournalStatus Status { get; set; } = NzPayFbtJournalStatus.AwaitingApproval;
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? PostedJournalId { get; set; }
    public PostedJournal? PostedJournal { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    [MaxLength(450)] public string? DecidedByUserId { get; set; }
    [MaxLength(500)] public string? DecisionReason { get; set; }
    public List<NzPayFbtJournalDraftLine> Lines { get; set; } = [];
}

public sealed class NzPayFbtJournalDraftLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DraftId { get; set; }
    public NzPayFbtJournalDraft Draft { get; set; } = null!;
    public Guid LedgerAccountId { get; set; }
    public LedgerAccount LedgerAccount { get; set; } = null!;
    [MaxLength(300)] public required string Description { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
}
