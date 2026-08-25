using System.ComponentModel.DataAnnotations;

namespace FijiAccounts.Web.Data;

public sealed class MobileIdempotencyRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrganisationId { get; set; }

    [MaxLength(450)]
    public required string UserId { get; set; }

    [MaxLength(128)]
    public required string Key { get; set; }

    [MaxLength(160)]
    public required string Operation { get; set; }

    [MaxLength(64)]
    public required string RequestHash { get; set; }

    public int StatusCode { get; set; }

    [MaxLength(80)]
    public string? ResultCode { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class MobileDeviceSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(450)]
    public required string UserId { get; set; }

    public Guid InstallationId { get; set; }

    [MaxLength(20)]
    public required string Platform { get; set; }

    [MaxLength(32)]
    public required string AppVersion { get; set; }

    [MaxLength(120)]
    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? RevokedAt { get; set; }

    [MaxLength(450)]
    public string? RevokedByUserId { get; set; }
}
