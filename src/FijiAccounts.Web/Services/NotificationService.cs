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
    ApplicationDbContext db)
{
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

        return notification;
    }


    public async Task<List<Notification>> GetUnreadAsync(
        Guid organisationId,
        CancellationToken ct = default)
    {
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

        if (duplicates.Count == 0)
        {
            return;
        }

        db.Notifications.RemoveRange(
            duplicates);

        await db.SaveChangesAsync(ct);
    }
    public async Task ResolveSalesInvoiceNotificationsAsync(
        Guid invoiceId,
        CancellationToken ct = default)
    {
        var notifications =
            await db.Notifications
                .Where(
                    x =>
                        x.RelatedEntityType == "SalesInvoice" &&
                        x.RelatedEntityId == invoiceId.ToString() &&
                        x.Status != NotificationStatus.Resolved)
                .ToListAsync(ct);

        if (notifications.Count == 0)
        {
            return;
        }

        foreach (var notification in notifications)
        {
            notification.Status =
                NotificationStatus.Resolved;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task ResolveSupplierBillNotificationsAsync(
        Guid billId,
        CancellationToken ct = default)
    {
        var notifications =
            await db.Notifications
                .Where(
                    x =>
                        x.RelatedEntityType == "SupplierBill" &&
                        x.RelatedEntityId == billId.ToString() &&
                        x.Status != NotificationStatus.Resolved)
                .ToListAsync(ct);

        if (notifications.Count == 0)
        {
            return;
        }

        foreach (var notification in notifications)
        {
            notification.Status =
                NotificationStatus.Resolved;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<int> GetUnreadCountAsync(
        Guid organisationId,
        CancellationToken ct = default)
    {
        return await db.Notifications
            .CountAsync(
                x =>
                    x.OrganisationId == organisationId &&
                    !x.IsRead,
                ct);
    }

    public async Task AcknowledgeAsync(
        Guid notificationId,
        string userId,
        CancellationToken ct = default)
    {
        var notification =
            await db.Notifications
                .SingleOrDefaultAsync(
                    x => x.Id == notificationId,
                    ct);

        if (notification is null)
        {
            return;
        }

        notification.Status =
            NotificationStatus.Acknowledged;

        notification.AcknowledgedAt =
            DateTimeOffset.UtcNow;

        notification.AcknowledgedByUserId =
            userId;

        await db.SaveChangesAsync(ct);
    }


    public async Task ResolveAsync(
        Guid notificationId,
        string userId,
        CancellationToken ct = default)
    {
        var notification =
            await db.Notifications
                .SingleOrDefaultAsync(
                    x => x.Id == notificationId,
                    ct);

        if (notification is null)
        {
            return;
        }

        notification.Status =
            NotificationStatus.Resolved;

        notification.ResolvedAt =
            DateTimeOffset.UtcNow;

        notification.ResolvedByUserId =
            userId;

        notification.IsRead = true;

        notification.ReadAt =
            DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    public async Task MarkAsReadAsync(
        Guid notificationId,
        CancellationToken ct = default)
    {
        var notification =
            await db.Notifications
                .SingleOrDefaultAsync(
                    x => x.Id == notificationId,
                    ct);

        if (notification is null)
        {
            return;
        }

        notification.IsRead = true;
        notification.ReadAt =
            DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
    }
}
