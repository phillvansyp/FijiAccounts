using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record CreateNotificationRequest(
    Guid OrganisationId,
    string Title,
    string Message,
    NotificationType Type,
    NotificationSeverity Severity,
    string? RelatedEntityType = null,
    string? RelatedEntityId = null,
    decimal? Amount = null,
    string? Currency = null);


public sealed class NotificationService(
    ApplicationDbContext db,
    OrganisationUpdateBroker updates,
    TenantAccessService access)
{
    public void PublishOrganisationUpdate(Guid organisationId) =>
        updates.Publish(organisationId);

    public async Task<Notification> CreateAsync(
        CreateNotificationRequest request,
        CancellationToken ct = default)
    {
        var notification =
            new Notification
            {
                OrganisationId = request.OrganisationId,
                Title = request.Title,
                Message = request.Message,
                Type = request.Type,
                Severity = request.Severity,
                RelatedEntityType = request.RelatedEntityType,
                RelatedEntityId = request.RelatedEntityId,
                Amount = request.Amount,
                Currency = request.Currency
            };

        db.Notifications.Add(notification);

        await db.SaveChangesAsync(ct);

        updates.Publish(request.OrganisationId);

        return notification;
    }


    public async Task<List<Notification>> GetUnreadAsync(
        string userId,
        Guid organisationId,
        CancellationToken ct = default)
    {
        await RequireAccessAsync(userId, organisationId);
        var notifications =
            await db.Notifications
                .AsNoTracking()
                .Where(
                    x =>
                        x.OrganisationId == organisationId &&
                        !x.IsRead)
                .ToListAsync(ct);

        return notifications
            .OrderByDescending(
                x => x.CreatedAt)
            .ToList();
    }


    public async Task RemoveDuplicateDocumentExpiryNotificationsAsync(
        CancellationToken ct = default)
    {
        var notifications =
            await db.Notifications
                .Where(
                    x =>
                        x.Type == NotificationType.DocumentExpiry &&
                        !x.IsRead)
                .ToListAsync(ct);

        var duplicates =
            notifications
                .GroupBy(
                    x => x.RelatedEntityId)
                .SelectMany(
                    group =>
                        group
                            .OrderByDescending(
                                x => x.CreatedAt)
                            .Skip(1))
                .ToList();

        var organisationIds =
            duplicates
                .Select(x => x.OrganisationId)
                .Distinct()
                .ToArray();

        if (duplicates.Count == 0)
        {
            return;
        }

        db.Notifications.RemoveRange(
            duplicates);

        await db.SaveChangesAsync(ct);

        foreach (var organisationId in organisationIds)
        {
            updates.Publish(organisationId);
        }
    }
    public async Task ResolveSalesInvoiceNotificationsAsync(
        Guid organisationId,
        Guid invoiceId,
        bool publishUpdate = true,
        CancellationToken ct = default)
    {
        var notifications =
            await db.Notifications
                .Where(
                    x =>
                        x.OrganisationId == organisationId &&
                        x.RelatedEntityType == "SalesInvoice" &&
                        x.RelatedEntityId == invoiceId.ToString() &&
                        x.Status != NotificationStatus.Resolved)
                .ToListAsync(ct);

        if (notifications.Count == 0)
        {
            if (publishUpdate)
            {
                updates.Publish(organisationId);
            }

            return;
        }

        var resolvedAt =
            DateTimeOffset.UtcNow;

        foreach (var notification in notifications)
        {
            notification.Status =
                NotificationStatus.Resolved;
            notification.ResolvedAt = resolvedAt;
            notification.IsRead = true;
            notification.ReadAt = resolvedAt;
        }

        await db.SaveChangesAsync(ct);

        if (publishUpdate)
        {
            updates.Publish(organisationId);
        }
    }

    public async Task ResolveSupplierBillNotificationsAsync(
        Guid organisationId,
        Guid billId,
        bool publishUpdate = true,
        CancellationToken ct = default)
    {
        var notifications =
            await db.Notifications
                .Where(
                    x =>
                        x.OrganisationId == organisationId &&
                        x.RelatedEntityType == "SupplierBill" &&
                        x.RelatedEntityId == billId.ToString() &&
                        x.Status != NotificationStatus.Resolved)
                .ToListAsync(ct);

        if (notifications.Count == 0)
        {
            if (publishUpdate)
            {
                updates.Publish(organisationId);
            }

            return;
        }

        var resolvedAt =
            DateTimeOffset.UtcNow;

        foreach (var notification in notifications)
        {
            notification.Status =
                NotificationStatus.Resolved;
            notification.ResolvedAt = resolvedAt;
            notification.IsRead = true;
            notification.ReadAt = resolvedAt;
        }

        await db.SaveChangesAsync(ct);

        if (publishUpdate)
        {
            updates.Publish(organisationId);
        }
    }

    public async Task<int> GetUnreadCountAsync(
        string userId,
        Guid organisationId,
        CancellationToken ct = default)
    {
        await RequireAccessAsync(userId, organisationId);
        return await db.Notifications
            .CountAsync(
                x =>
                    x.OrganisationId == organisationId &&
                    !x.IsRead,
                ct);
    }

    public async Task AcknowledgeAsync(
        string userId,
        Guid organisationId,
        Guid notificationId,
        CancellationToken ct = default)
    {
        await RequireAccessAsync(userId, organisationId);
        var notification =
            await db.Notifications
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == notificationId &&
                        x.OrganisationId == organisationId,
                    ct);

        if (notification is null ||
            notification.Status != NotificationStatus.Open)
        {
            return;
        }

        var wasRead = notification.IsRead;

        notification.Status =
            NotificationStatus.Acknowledged;

        notification.AcknowledgedAt =
            DateTimeOffset.UtcNow;

        notification.AcknowledgedByUserId =
            userId;

        db.AuditEvents.Add(NotificationAudit(
            notification,
            userId,
            "NotificationAcknowledged",
            NotificationStatus.Open,
            NotificationStatus.Acknowledged,
            wasRead,
            wasRead));

        await db.SaveChangesAsync(ct);

        updates.Publish(notification.OrganisationId);
    }


    public async Task ResolveAsync(
        string userId,
        Guid organisationId,
        Guid notificationId,
        CancellationToken ct = default)
    {
        await RequireAccessAsync(userId, organisationId);
        var notification =
            await db.Notifications
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == notificationId &&
                        x.OrganisationId == organisationId,
                    ct);

        if (notification is null ||
            notification.Status == NotificationStatus.Resolved)
        {
            return;
        }

        var oldStatus = notification.Status;
        var wasRead = notification.IsRead;

        notification.Status =
            NotificationStatus.Resolved;

        notification.ResolvedAt =
            DateTimeOffset.UtcNow;

        notification.ResolvedByUserId =
            userId;

        notification.IsRead = true;

        notification.ReadAt =
            DateTimeOffset.UtcNow;

        db.AuditEvents.Add(NotificationAudit(
            notification,
            userId,
            "NotificationResolved",
            oldStatus,
            NotificationStatus.Resolved,
            wasRead,
            true));

        await db.SaveChangesAsync(ct);

        updates.Publish(notification.OrganisationId);
    }

    public async Task<int> GetFinancialAlertCountAsync(
        string userId,
        Guid organisationId,
        CancellationToken ct = default)
    {
        await RequireAccessAsync(userId, organisationId);
        return await db.Notifications.CountAsync(
            x =>
                x.OrganisationId == organisationId &&
                x.Status == NotificationStatus.Open &&
                (
                    x.Type == NotificationType.PaymentDueSoon ||
                    x.Type == NotificationType.PaymentOverdue
                ),
            ct);
    }


    public async Task<int> GetDocumentAlertCountAsync(
        string userId,
        Guid organisationId,
        CancellationToken ct = default)
    {
        await RequireAccessAsync(userId, organisationId);
        return await db.Notifications.CountAsync(
            x =>
                x.OrganisationId == organisationId &&
                x.Status == NotificationStatus.Open &&
                x.Type == NotificationType.DocumentExpiry,
            ct);
    }


    public async Task<int> GetSystemAlertCountAsync(
        string userId,
        Guid organisationId,
        CancellationToken ct = default)
    {
        await RequireAccessAsync(userId, organisationId);
        return await db.Notifications.CountAsync(
            x =>
                x.OrganisationId == organisationId &&
                x.Status == NotificationStatus.Open &&
                x.Type == NotificationType.System,
            ct);
    }

    private async Task RequireAccessAsync(
        string userId,
        Guid organisationId)
    {
        if (string.IsNullOrWhiteSpace(userId) ||
            await access.FindAsync(userId, organisationId) is null)
        {
            throw new UnauthorizedAccessException(
                "You do not have access to this organisation's notifications.");
        }
    }

    private static AuditEvent NotificationAudit(
        Notification notification,
        string userId,
        string eventType,
        NotificationStatus oldStatus,
        NotificationStatus newStatus,
        bool wasRead,
        bool isRead) =>
        new()
        {
            OrganisationId = notification.OrganisationId,
            UserId = userId,
            EventType = eventType,
            EntityType = nameof(Notification),
            EntityId = notification.Id.ToString(),
            JsonData = JsonSerializer.Serialize(new
            {
                Type = notification.Type.ToString(),
                Severity = notification.Severity.ToString(),
                notification.RelatedEntityType,
                notification.RelatedEntityId,
                OldStatus = oldStatus.ToString(),
                NewStatus = newStatus.ToString(),
                WasRead = wasRead,
                IsRead = isRead
            })
        };
}
