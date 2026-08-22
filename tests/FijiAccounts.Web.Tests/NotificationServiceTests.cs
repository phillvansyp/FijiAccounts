using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class NotificationServiceTests
{
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
            notification.Id,
            test.UserId);

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
            notification.Id,
            test.UserId);

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
