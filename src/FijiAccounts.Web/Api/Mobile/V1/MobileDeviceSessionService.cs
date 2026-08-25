using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Api.Mobile.V1;

public enum MobileDeviceRegistrationStatus
{
    Registered,
    Revoked
}

public sealed record MobileDeviceRegistrationResult(
    MobileDeviceRegistrationStatus Status,
    MobileDeviceSessionSummary Device);

public sealed class MobileDeviceSessionService(ApplicationDbContext db)
{
    public async Task<MobileDeviceRegistrationResult> RegisterAsync(
        string userId,
        MobileClientRequest client,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        var session = await db.MobileDeviceSessions.SingleOrDefaultAsync(device =>
            device.UserId == userId && device.InstallationId == client.InstallationId,
            cancellationToken);
        if (session?.RevokedAt is not null)
        {
            return new(MobileDeviceRegistrationStatus.Revoked, Summary(session, true));
        }

        var now = DateTimeOffset.UtcNow;
        if (session is null)
        {
            session = new MobileDeviceSession
            {
                UserId = userId,
                InstallationId = client.InstallationId,
                Platform = client.Platform,
                AppVersion = client.Version.ToString(),
                DisplayName = NormalizeDisplayName(displayName),
                CreatedAt = now,
                LastSeenAt = now
            };
            db.MobileDeviceSessions.Add(session);
        }
        else
        {
            session.Platform = client.Platform;
            session.AppVersion = client.Version.ToString();
            session.DisplayName = NormalizeDisplayName(displayName) ?? session.DisplayName;
            session.LastSeenAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new(MobileDeviceRegistrationStatus.Registered, Summary(session, true));
    }

    public async Task<IReadOnlyList<MobileDeviceSessionSummary>> ListAsync(
        string userId,
        Guid currentInstallationId,
        CancellationToken cancellationToken = default) =>
        (await db.MobileDeviceSessions
            .AsNoTracking()
            .Where(device => device.UserId == userId)
            .ToListAsync(cancellationToken))
        .OrderByDescending(device => device.LastSeenAt)
        .Select(device => Summary(
            device,
            device.InstallationId == currentInstallationId))
        .ToList();

    public async Task<bool> RevokeAsync(
        string userId,
        Guid deviceSessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await db.MobileDeviceSessions.SingleOrDefaultAsync(device =>
            device.Id == deviceSessionId && device.UserId == userId,
            cancellationToken);
        if (session is null)
        {
            return false;
        }

        if (session.RevokedAt is null)
        {
            session.RevokedAt = DateTimeOffset.UtcNow;
            session.RevokedByUserId = userId;
            await db.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task<MobileDeviceState> GetStateAsync(
        string userId,
        Guid installationId,
        CancellationToken cancellationToken = default)
    {
        var session = await db.MobileDeviceSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(device =>
                device.UserId == userId && device.InstallationId == installationId,
                cancellationToken);
        return new MobileDeviceState(
            session is not null,
            session?.RevokedAt is not null);
    }

    private static string? NormalizeDisplayName(string? displayName)
    {
        var normalized = displayName?.Trim();
        if (normalized?.Length > 120)
        {
            throw new ArgumentException("Device display name cannot exceed 120 characters.");
        }

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static MobileDeviceSessionSummary Summary(
        MobileDeviceSession session,
        bool isCurrent) =>
        new(
            session.Id,
            session.InstallationId,
            session.Platform,
            session.AppVersion,
            session.DisplayName,
            session.CreatedAt,
            session.LastSeenAt,
            session.RevokedAt,
            isCurrent);
}
