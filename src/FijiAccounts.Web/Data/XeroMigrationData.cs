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
    public List<XeroMigrationRecord> Records { get; set; } = [];
    public List<XeroMigrationMapping> Mappings { get; set; } = [];
    public List<XeroMigrationIssue> Issues { get; set; } = [];
    public List<XeroMigrationImportRun> ImportRuns { get; set; } = [];
}

public enum XeroMigrationRecordStatus
{
    Ready = 0,
    NeedsMapping = 1,
    NeedsReview = 2,
    Unsupported = 3,
    Imported = 4,
    Skipped = 5,
    Failed = 6,
    /// <summary>
    /// The source object is retained in the immutable Xero archive but is not
    /// replayed into the live ledger.  Cutover balances replace historical
    /// posting so the opening position is not counted twice.
    /// </summary>
    Archived = 7
}

public enum XeroMigrationMappingKind
{
    Account = 0,
    TaxRate = 1,
    TrackingCategory = 2,
    TrackingOption = 3,
    Contact = 4,
    Currency = 5,
    Item = 6,
    AssetType = 7
}

public enum XeroMigrationMappingStatus
{
    Suggested = 0,
    Confirmed = 1,
    Ignored = 2
}

public enum XeroMigrationIssueSeverity
{
    Information = 0,
    Warning = 1,
    Blocking = 2
}

public enum XeroMigrationImportRunStatus
{
    Validating = 0,
    Blocked = 1,
    Ready = 2,
    Importing = 3,
    Completed = 4,
    Failed = 5
}

/// <summary>
/// A normalised, reviewable Xero object. The immutable API snapshot remains the
/// source of truth; this row supplies identity, validation and target linkage.
/// </summary>
public sealed class XeroMigrationRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Guid XeroMigrationId { get; set; }
    public XeroMigration Migration { get; set; } = null!;
    public Guid XeroApiSnapshotId { get; set; }
    public XeroApiSnapshot Snapshot { get; set; } = null!;
    [MaxLength(80)] public required string DatasetKey { get; set; }
    [MaxLength(160)] public required string ExternalId { get; set; }
    [MaxLength(240)] public required string DisplayName { get; set; }
    [MaxLength(64)] public required string SourceHash { get; set; }
    public DateOnly? SourceDate { get; set; }
    public XeroMigrationRecordStatus Status { get; set; }
    [MaxLength(80)] public string? TargetEntityType { get; set; }
    public Guid? TargetEntityId { get; set; }
    [MaxLength(1000)] public string? ReviewMessage { get; set; }
    public DateTimeOffset PreparedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string PreparedByUserId { get; set; }
    public DateTimeOffset? ImportedAt { get; set; }
    [MaxLength(450)] public string? ImportedByUserId { get; set; }
}

/// <summary>Maps a Xero code or identifier to an Account Island record.</summary>
public sealed class XeroMigrationMapping
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Guid XeroMigrationId { get; set; }
    public XeroMigration Migration { get; set; } = null!;
    public XeroMigrationMappingKind Kind { get; set; }
    [MaxLength(160)] public required string SourceKey { get; set; }
    [MaxLength(240)] public required string SourceName { get; set; }
    [MaxLength(80)] public string? TargetEntityType { get; set; }
    public Guid? TargetEntityId { get; set; }
    [MaxLength(160)] public string? TargetDisplayName { get; set; }
    public XeroMigrationMappingStatus Status { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string UpdatedByUserId { get; set; }
}

public sealed class XeroMigrationIssue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Guid XeroMigrationId { get; set; }
    public XeroMigration Migration { get; set; } = null!;
    public Guid? XeroMigrationRecordId { get; set; }
    public XeroMigrationRecord? Record { get; set; }
    [MaxLength(80)] public required string Code { get; set; }
    public XeroMigrationIssueSeverity Severity { get; set; }
    [MaxLength(1000)] public required string Message { get; set; }
    public bool IsResolved { get; set; }
    [MaxLength(500)] public string? Resolution { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }
    [MaxLength(450)] public string? ResolvedByUserId { get; set; }
}

public sealed class XeroMigrationImportRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Guid XeroMigrationId { get; set; }
    public XeroMigration Migration { get; set; } = null!;
    public XeroMigrationImportRunStatus Status { get; set; }
    public bool IsDryRun { get; set; } = true;
    public int SourceRecordCount { get; set; }
    public int ReadyCount { get; set; }
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
    public int WarningCount { get; set; }
    public int BlockingCount { get; set; }
    [MaxLength(2000)] public string? Summary { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string StartedByUserId { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
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
    public DateTimeOffset? LastFullReconciliationAt { get; set; }
    public DateTimeOffset? NextSyncAt { get; set; }
    public DateTimeOffset? SyncLeaseUntil { get; set; }
    public bool ContinuousSyncEnabled { get; set; }
    public int ConsecutiveSyncFailures { get; set; }
    [MaxLength(2000)] public string? LastSyncError { get; set; }
}

/// <summary>
/// A read-only local projection of a Xero object. Xero remains authoritative;
/// Account Island never sends changes from this record back to Xero.
/// </summary>
public sealed class XeroMirrorRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Guid XeroConnectionId { get; set; }
    public XeroConnection Connection { get; set; } = null!;
    [MaxLength(80)] public required string DatasetKey { get; set; }
    [MaxLength(160)] public required string ExternalId { get; set; }
    [MaxLength(240)] public required string DisplayName { get; set; }
    [MaxLength(64)] public required string SourceHash { get; set; }
    public required string SourceJson { get; set; }
    public DateTimeOffset? SourceUpdatedAt { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsArchived { get; set; }
}

/// <summary>Stable link between a Xero source identity and a safe Account Island master record.</summary>
public sealed class XeroMirrorEntityLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Guid XeroConnectionId { get; set; }
    public XeroConnection Connection { get; set; } = null!;
    [MaxLength(80)] public required string DatasetKey { get; set; }
    [MaxLength(160)] public required string ExternalId { get; set; }
    [MaxLength(80)] public required string TargetEntityType { get; set; }
    public Guid TargetEntityId { get; set; }
    public DateTimeOffset LinkedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum XeroReadOnlySyncRunStatus
{
    Running,
    Completed,
    Partial,
    Failed
}

public sealed class XeroReadOnlySyncRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Guid XeroConnectionId { get; set; }
    public XeroConnection Connection { get; set; } = null!;
    [MaxLength(40)] public required string Trigger { get; set; }
    public XeroReadOnlySyncRunStatus Status { get; set; } = XeroReadOnlySyncRunStatus.Running;
    public bool IsFullReconciliation { get; set; }
    public int DatasetCount { get; set; }
    public int RecordCount { get; set; }
    public int ChangedCount { get; set; }
    public int ArchivedCount { get; set; }
    [MaxLength(2000)] public string? ErrorSummary { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
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
