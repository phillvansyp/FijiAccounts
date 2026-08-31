using System.ComponentModel.DataAnnotations;

namespace FijiAccounts.Web.Data;

public sealed class ImmutableDocumentObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    [MaxLength(40)] public required string Provider { get; set; }
    [MaxLength(500)] public required string ObjectKey { get; set; }
    [MaxLength(64)] public required string Sha256 { get; set; }
    public long ContentLength { get; set; }
    public required byte[] Content { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string CreatedByUserId { get; set; }
}

public sealed class YearEndHandoverPackSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Guid AccountingPeriodId { get; set; }
    public AccountingPeriod AccountingPeriod { get; set; } = null!;
    public int Version { get; set; }
    [MaxLength(180)] public required string FileName { get; set; }
    public Guid ImmutableDocumentObjectId { get; set; }
    public ImmutableDocumentObject ImmutableDocumentObject { get; set; } = null!;
    [MaxLength(64)] public required string Sha256 { get; set; }
    public long ContentLength { get; set; }
    [MaxLength(64)] public required string ManifestSha256 { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string CreatedByUserId { get; set; }
    [MaxLength(80)] public string? ReviewApprovalReference { get; set; }
    public DateTimeOffset? ReviewApprovedAt { get; set; }
    public string? ReviewApprovedByUserId { get; set; }
}

public enum ImmutableDocumentIntegrityStatus
{
    Healthy,
    AttentionRequired
}

public sealed class ImmutableDocumentIntegrityScan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public int ObjectCount { get; set; }
    public int LinkedDocumentCount { get; set; }
    public int VerifiedObjectCount { get; set; }
    public int IntegrityFailureCount { get; set; }
    public int MissingObjectReferenceCount { get; set; }
    public int LegacyDocumentCount { get; set; }
    public int UnreferencedObjectCount { get; set; }
    public ImmutableDocumentIntegrityStatus Status { get; set; }
    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;
    public long CompletedAtTicks { get; set; } = DateTimeOffset.UtcNow.UtcTicks;
    [MaxLength(450)] public required string CompletedByUserId { get; set; }
}
