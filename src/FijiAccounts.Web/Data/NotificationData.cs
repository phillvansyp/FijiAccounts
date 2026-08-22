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
    System
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

    public bool IsRead { get; set; }

    public DateTimeOffset? ReadAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } =
        DateTimeOffset.UtcNow;
}
