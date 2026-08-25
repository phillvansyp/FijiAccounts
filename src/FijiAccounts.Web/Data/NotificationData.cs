using System.ComponentModel.DataAnnotations;

namespace FijiAccounts.Web.Data;

public enum NotificationSeverity
{
    Info,
    Warning,
    Critical
}

public enum NotificationType
{
    DocumentExpiry,
    PaymentDue,
    PaymentOverdue,
    System,
    PaymentDueSoon
}

public enum NotificationStatus
{
    Open,
    Acknowledged,
    Resolved
}

public sealed class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrganisationId { get; set; }

    [MaxLength(120)]
    public required string Title { get; set; }

    [MaxLength(500)]
    public required string Message { get; set; }

    public NotificationType Type { get; set; }

    public NotificationSeverity Severity { get; set; }

    [MaxLength(80)]
    public string? RelatedEntityType { get; set; }

    [MaxLength(80)]
    public string? RelatedEntityId { get; set; }

    public decimal? Amount { get; set; }

    [MaxLength(3)]
    public string? Currency { get; set; }

    public bool IsRead { get; set; }

    public DateTimeOffset? ReadAt { get; set; }

    public NotificationStatus Status { get; set; } =
        NotificationStatus.Open;

    public DateTimeOffset? AcknowledgedAt { get; set; }

    [MaxLength(450)]
    public string? AcknowledgedByUserId { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    [MaxLength(450)]
    public string? ResolvedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } =
        DateTimeOffset.UtcNow;

    public long CreatedAtTicks { get; set; } =
        DateTimeOffset.UtcNow.UtcTicks;
}
