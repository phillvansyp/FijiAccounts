using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Data;

// Receipt access is separate from ledger membership: submitting a receipt never grants accounting access.
[Index(nameof(OrganisationId), nameof(UserId), IsUnique = true)]
public sealed class ReceiptContributor
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    [MaxLength(450)] public required string UserId { get; set; }
    public bool Active { get; set; } = true;
}

[Index(nameof(OrganisationId), nameof(SubmittedByUserId), nameof(RequestId), IsUnique = true)]
public sealed class EmployeeReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    [MaxLength(450)] public required string SubmittedByUserId { get; set; }
    public Guid RequestId { get; set; }
    [MaxLength(160)] public required string Merchant { get; set; }
    [MaxLength(1000)] public required string Purpose { get; set; }
    public DateOnly ReceiptDate { get; set; }
    public decimal Amount { get; set; }
    [MaxLength(3)] public required string Currency { get; set; }
    public bool PaidPersonally { get; set; }
    [MaxLength(32)] public string Status { get; set; } = "Submitted";
    [MaxLength(1000)] public string? ReviewNote { get; set; }
    [MaxLength(450)] public string? ReviewedByUserId { get; set; }
    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReviewedAt { get; set; }
    public Guid DocumentId { get; set; }
    [MaxLength(255)] public required string FileName { get; set; }
    [MaxLength(80)] public required string ContentType { get; set; }
    [ConcurrencyCheck] public int Version { get; set; }
}
