using System.Text.Json;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class NotificationServiceTests
{
    [Fact]
    public async Task MarkRead_IsAuditedAndIdempotentWithoutResolvingNotification()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var notification = await CreateNotificationAsync(test.Notifications, test);

        Assert.True(await test.Notifications.MarkReadAsync(
            test.UserId,
            test.Organisation.Id,
            notification.Id));
        Assert.True(await test.Notifications.MarkReadAsync(
            test.UserId,
            test.Organisation.Id,
            notification.Id));

        var saved = await test.Db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.Id == notification.Id);
        Assert.True(saved.IsRead);
        Assert.NotNull(saved.ReadAt);
        Assert.Equal(NotificationStatus.Open, saved.Status);

        var audit = Assert.Single(await test.Db.AuditEvents.AsNoTracking().ToListAsync());
        Assert.Equal("NotificationRead", audit.EventType);
        using var evidence = JsonDocument.Parse(audit.JsonData);
        Assert.Equal("Open", evidence.RootElement.GetProperty("OldStatus").GetString());
        Assert.Equal("Open", evidence.RootElement.GetProperty("NewStatus").GetString());
        Assert.False(evidence.RootElement.GetProperty("WasRead").GetBoolean());
        Assert.True(evidence.RootElement.GetProperty("IsRead").GetBoolean());
    }

    [Fact]
    public async Task Acknowledge_RecordsUserAndKeepsNotificationUnread()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service = test.Notifications;
        var notification = await CreateNotificationAsync(service, test);
        var updates = new List<Guid>();
        using var subscription =
            test.Updates.Subscribe(updates.Add);

        await service.AcknowledgeAsync(
            test.UserId,
            test.Organisation.Id,
            notification.Id);

        var saved =
            await test.Db.Notifications
                .AsNoTracking()
                .SingleAsync(x => x.Id == notification.Id);

        Assert.Equal(NotificationStatus.Acknowledged, saved.Status);
        Assert.Equal(test.UserId, saved.AcknowledgedByUserId);
        Assert.NotNull(saved.AcknowledgedAt);
        Assert.False(saved.IsRead);
        Assert.Null(saved.ReadAt);
        Assert.Equal([test.Organisation.Id], updates);

        var audit = Assert.Single(await test.Db.AuditEvents.AsNoTracking().ToListAsync());
        Assert.Equal("NotificationAcknowledged", audit.EventType);
        Assert.Equal(nameof(Notification), audit.EntityType);
        Assert.Equal(notification.Id.ToString(), audit.EntityId);
        using var evidence = JsonDocument.Parse(audit.JsonData);
        Assert.Equal("Open", evidence.RootElement.GetProperty("OldStatus").GetString());
        Assert.Equal("Acknowledged", evidence.RootElement.GetProperty("NewStatus").GetString());
        Assert.DoesNotContain(notification.Title, audit.JsonData, StringComparison.Ordinal);
        Assert.DoesNotContain(notification.Message, audit.JsonData, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolve_RecordsUserAndMarksNotificationRead()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service = test.Notifications;
        var notification = await CreateNotificationAsync(service, test);
        var updates = new List<Guid>();
        using var subscription =
            test.Updates.Subscribe(updates.Add);

        await service.ResolveAsync(
            test.UserId,
            test.Organisation.Id,
            notification.Id);

        var saved =
            await test.Db.Notifications
                .AsNoTracking()
                .SingleAsync(x => x.Id == notification.Id);

        Assert.Equal(NotificationStatus.Resolved, saved.Status);
        Assert.Equal(test.UserId, saved.ResolvedByUserId);
        Assert.NotNull(saved.ResolvedAt);
        Assert.True(saved.IsRead);
        Assert.NotNull(saved.ReadAt);
        Assert.Equal([test.Organisation.Id], updates);

        var audit = Assert.Single(await test.Db.AuditEvents.AsNoTracking().ToListAsync());
        Assert.Equal("NotificationResolved", audit.EventType);
        using var evidence = JsonDocument.Parse(audit.JsonData);
        Assert.Equal("Open", evidence.RootElement.GetProperty("OldStatus").GetString());
        Assert.Equal("Resolved", evidence.RootElement.GetProperty("NewStatus").GetString());
        Assert.False(evidence.RootElement.GetProperty("WasRead").GetBoolean());
        Assert.True(evidence.RootElement.GetProperty("IsRead").GetBoolean());
    }

    [Fact]
    public async Task Create_PublishesOrganisationUpdate()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var updates = new List<Guid>();
        using var subscription =
            test.Updates.Subscribe(updates.Add);

        await CreateNotificationAsync(test.Notifications, test);

        Assert.Equal([test.Organisation.Id], updates);
    }

    [Fact]
    public async Task ResolveInvoice_PublishesEvenWhenNoAlertExists()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var updates = new List<Guid>();
        using var subscription =
            test.Updates.Subscribe(updates.Add);

        await test.Notifications.ResolveSalesInvoiceNotificationsAsync(
            test.Organisation.Id,
            Guid.NewGuid());

        Assert.Equal([test.Organisation.Id], updates);
    }

    [Fact]
    public async Task ResolveInvoice_MarksMatchingAlertsResolvedAndRead()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoiceId = Guid.NewGuid();
        var notification =
            await test.Notifications.CreateAsync(
                new CreateNotificationRequest(
                    test.Organisation.Id,
                    "Invoice overdue",
                    "Payment is overdue.",
                    NotificationType.PaymentOverdue,
                    NotificationSeverity.Warning,
                    RelatedEntityType: "SalesInvoice",
                    RelatedEntityId: invoiceId.ToString()));

        await test.Notifications.ResolveSalesInvoiceNotificationsAsync(
            test.Organisation.Id,
            invoiceId);

        var saved =
            await test.Db.Notifications
                .AsNoTracking()
                .SingleAsync(x => x.Id == notification.Id);

        Assert.Equal(NotificationStatus.Resolved, saved.Status);
        Assert.NotNull(saved.ResolvedAt);
        Assert.True(saved.IsRead);
        Assert.NotNull(saved.ReadAt);
    }

    [Fact]
    public async Task UserLifecycleSuppressesNoOps()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var notification = await CreateNotificationAsync(test.Notifications, test);
        var updates = new List<Guid>();
        using var subscription = test.Updates.Subscribe(updates.Add);

        await test.Notifications.AcknowledgeAsync(
            test.UserId,
            test.Organisation.Id,
            notification.Id);
        await test.Notifications.AcknowledgeAsync(
            test.UserId,
            test.Organisation.Id,
            notification.Id);
        await test.Notifications.ResolveAsync(
            test.UserId,
            test.Organisation.Id,
            notification.Id);
        await test.Notifications.ResolveAsync(
            test.UserId,
            test.Organisation.Id,
            notification.Id);

        Assert.Equal(
            ["NotificationAcknowledged", "NotificationResolved"],
            await test.Db.AuditEvents
                .AsNoTracking()
                .OrderBy(x => x.Id)
                .Select(x => x.EventType)
                .ToListAsync());
        Assert.Equal([test.Organisation.Id, test.Organisation.Id], updates);
    }

    [Fact]
    public async Task UnauthorizedAndCrossTenantRequestsCannotReadOrMutateNotifications()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var ownNotification = await CreateNotificationAsync(test.Notifications, test);
        var otherOrganisation = new Organisation
        {
            LegalName = "Other Notification Tenant Limited",
            Kind = OrganisationKind.Business
        };
        test.Db.Organisations.Add(otherOrganisation);
        var otherNotification = await test.Notifications.CreateAsync(
            new CreateNotificationRequest(
                otherOrganisation.Id,
                "Other tenant notification",
                "Must remain inaccessible.",
                NotificationType.System,
                NotificationSeverity.Warning));
        var auditCount = await test.Db.AuditEvents.CountAsync();
        var unrelatedUserId = Guid.NewGuid().ToString();

        var unread = await test.Notifications.GetUnreadAsync(
            test.UserId,
            test.Organisation.Id);
        Assert.Equal(ownNotification.Id, Assert.Single(unread).Id);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            test.Notifications.GetUnreadAsync(unrelatedUserId, test.Organisation.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            test.Notifications.GetUnreadCountAsync(unrelatedUserId, test.Organisation.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            test.Notifications.AcknowledgeAsync(
                unrelatedUserId,
                test.Organisation.Id,
                ownNotification.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            test.Notifications.ResolveAsync(
                test.UserId,
                otherOrganisation.Id,
                otherNotification.Id));

        await test.Notifications.AcknowledgeAsync(
            test.UserId,
            test.Organisation.Id,
            otherNotification.Id);
        await test.Notifications.ResolveAsync(
            test.UserId,
            test.Organisation.Id,
            otherNotification.Id);

        Assert.Equal(auditCount, await test.Db.AuditEvents.CountAsync());
        Assert.All(
            await test.Db.Notifications.AsNoTracking().ToListAsync(),
            notification => Assert.Equal(NotificationStatus.Open, notification.Status));
    }

    private static async Task<Notification> CreateNotificationAsync(
        NotificationService service,
        AccountingTestDatabase test)
    {
        return await service.CreateAsync(
            new CreateNotificationRequest(
                test.Organisation.Id,
                "Test notification",
                "Notification lifecycle test.",
                NotificationType.System,
                NotificationSeverity.Info));
    }
}
