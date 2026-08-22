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

        var service = new NotificationService(test.Db);
        var notification = await CreateNotificationAsync(service, test);

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
    }

    [Fact]
    public async Task Resolve_RecordsUserAndMarksNotificationRead()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service = new NotificationService(test.Db);
        var notification = await CreateNotificationAsync(service, test);

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
