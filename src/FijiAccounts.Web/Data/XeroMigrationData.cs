using System.ComponentModel.DataAnnotations;

namespace FijiAccounts.Web.Data;

public enum XeroMigrationStatus
{
    Preparing = 0,
    ReadyForImport = 1,
    Reconciliation = 2,
    ApprovedForCutover = 3,
    Complete = 4
}

public sealed class XeroMigration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    [MaxLength(160)] public string SourceOrganisationName { get; set; } = string.Empty;
    public DateOnly? CutoverDate { get; set; }
    public bool IncludeFullHistory { get; set; } = true;
    public XeroMigrationStatus Status { get; set; } = XeroMigrationStatus.Preparing;
    public bool XeroEntryStopped { get; set; }
    public bool TrialBalanceReconciled { get; set; }
    public bool ReceivablesReconciled { get; set; }
    public bool PayablesReconciled { get; set; }
    public bool BankReconciled { get; set; }
    public bool FixedAssetsReconciled { get; set; }
    public bool GstReconciled { get; set; }
    public bool ManagementApproved { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string CreatedByUserId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string UpdatedByUserId { get; set; }
    public List<XeroMigrationFile> Files { get; set; } = [];
    public List<XeroApiSnapshot> ApiSnapshots { get; set; } = [];
}

public sealed class XeroMigrationFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Guid XeroMigrationId { get; set; }
    public XeroMigration Migration { get; set; } = null!;
    [MaxLength(80)] public required string Category { get; set; }
    [MaxLength(255)] public required string FileName { get; set; }
    [MaxLength(100)] public required string ContentType { get; set; }
    public long Size { get; set; }
    [MaxLength(64)] public required string Sha256 { get; set; }
    public Guid ImmutableDocumentObjectId { get; set; }
    public ImmutableDocumentObject ImmutableDocumentObject { get; set; } = null!;
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string UploadedByUserId { get; set; }
}

public sealed class XeroConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    [MaxLength(100)] public string? XeroConnectionId { get; set; }
    public Guid? XeroTenantId { get; set; }
    [MaxLength(160)] public string? XeroTenantName { get; set; }
    public string TenantCandidatesJson { get; set; } = "[]";
    public required string ProtectedAccessToken { get; set; }
    public required string ProtectedRefreshToken { get; set; }
    public DateTimeOffset AccessTokenExpiresAt { get; set; }
    [MaxLength(2000)] public required string GrantedScopes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset ConnectedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string ConnectedByUserId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string UpdatedByUserId { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    [MaxLength(2000)] public string? LastSyncError { get; set; }
}

public sealed class XeroApiSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Guid XeroMigrationId { get; set; }
    public XeroMigration Migration { get; set; } = null!;
    [MaxLength(80)] public required string DatasetKey { get; set; }
    [MaxLength(160)] public required string DisplayName { get; set; }
    [MaxLength(500)] public required string Endpoint { get; set; }
    public int RecordCount { get; set; }
    public int PageCount { get; set; }
    public long Size { get; set; }
    [MaxLength(64)] public required string Sha256 { get; set; }
    public Guid ImmutableDocumentObjectId { get; set; }
    public ImmutableDocumentObject ImmutableDocumentObject { get; set; } = null!;
    public DateTimeOffset RetrievedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string RetrievedByUserId { get; set; }
}
