using System.ComponentModel.DataAnnotations;

namespace FijiAccounts.Web.Data;

public enum PayrollIslandImportStatus
{
    ReadyToPost,
    Posted,
    Superseded,
    CorrectionRequired
}

public enum PayrollPaymentKind
{
    NetWages,
    Paye,
    Fnpf,
    OtherDeduction
}

public enum PayrollPaymentStatus
{
    Expected,
    Paid,
    Cancelled
}

public sealed class PayrollIslandConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    [MaxLength(500)] public required string BaseUrl { get; set; }
    [MaxLength(120)] public required string PayrollOrganisationId { get; set; }
    public required string ProtectedAccessToken { get; set; }
    public Guid WagesExpenseAccountId { get; set; }
    public Guid EmployerContributionsExpenseAccountId { get; set; }
    public Guid NetWagesPayableAccountId { get; set; }
    public Guid PayePayableAccountId { get; set; }
    public Guid FnpfPayableAccountId { get; set; }
    public Guid OtherDeductionsPayableAccountId { get; set; }
    public bool IsActive { get; set; } = true;
    [MaxLength(500)] public string? LastSyncCursor { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    [MaxLength(1000)] public string? LastSyncError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string CreatedByUserId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string UpdatedByUserId { get; set; }
    public List<PayrollIslandPayRunImport> PayRuns { get; set; } = [];
}

public sealed class PayrollIslandPayRunImport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    public Guid ConnectionId { get; set; }
    public PayrollIslandConnection Connection { get; set; } = null!;
    [MaxLength(120)] public required string ExternalPayRunId { get; set; }
    public int Revision { get; set; }
    [MaxLength(80)] public required string PayRunNumber { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public DateOnly PaymentDate { get; set; }
    [MaxLength(3)] public required string Currency { get; set; }
    public int EmployeeCount { get; set; }
    public decimal GrossEarnings { get; set; }
    public decimal EmployeePaye { get; set; }
    public decimal EmployeeFnpf { get; set; }
    public decimal EmployerFnpf { get; set; }
    public decimal OtherDeductions { get; set; }
    public decimal NetPay { get; set; }
    [MaxLength(64)] public required string PayloadSha256 { get; set; }
    public PayrollIslandImportStatus Status { get; set; }
    public Guid? PostedJournalId { get; set; }
    public PostedJournal? PostedJournal { get; set; }
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public required string ImportedByUserId { get; set; }
    public List<PayrollIslandPaymentRecord> Payments { get; set; } = [];
}

public sealed class PayrollIslandPaymentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PayRunImportId { get; set; }
    public PayrollIslandPayRunImport PayRunImport { get; set; } = null!;
    [MaxLength(120)] public required string ExternalPaymentId { get; set; }
    public PayrollPaymentKind Kind { get; set; }
    public PayrollPaymentStatus Status { get; set; }
    public DateOnly DueDate { get; set; }
    public DateOnly? PaidDate { get; set; }
    public decimal Amount { get; set; }
    [MaxLength(160)] public string? Reference { get; set; }
}
