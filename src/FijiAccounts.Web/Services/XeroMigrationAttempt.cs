using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

internal static class XeroMigrationAttempt
{
    public static async Task<DateTimeOffset?> StartedAtAsync(
        ApplicationDbContext db,
        Guid organisationId,
        Guid migrationId,
        CancellationToken cancellationToken = default)
    {
        var events = await db.AuditEvents.AsNoTracking()
            .Where(x => x.OrganisationId == organisationId &&
                        x.EventType == "XeroMigrationRestarted" &&
                        x.EntityId == migrationId.ToString())
            .Select(x => x.OccurredAt)
            .ToListAsync(cancellationToken);
        return events.Count == 0 ? null : events.Max();
    }

    public static bool IsCurrent(DateTimeOffset occurredAt, DateTimeOffset? startedAt) =>
        startedAt is null || occurredAt >= startedAt.Value;
}
