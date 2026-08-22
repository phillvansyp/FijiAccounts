using Microsoft.EntityFrameworkCore;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed class DocumentExpiryWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<DocumentExpiryWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope =
                    scopeFactory.CreateScope();

                var db =
                    scope.ServiceProvider
                        .GetRequiredService<ApplicationDbContext>();

                var notifications =
                    scope.ServiceProvider
                        .GetRequiredService<NotificationService>();

                var today =
                    DateOnly.FromDateTime(
                        DateTime.Today);

                var limit =
                    today.AddDays(30);

                var documents =
                    await db.BusinessPartyDocuments
                        .AsNoTracking()
                        .Where(
                            x =>
                                x.ExpiryDate.HasValue &&
                                x.ExpiryDate.Value <= limit)
                        .ToListAsync(
                            stoppingToken);

                foreach (var document in documents)
                {
                    var exists =
                        await db.Notifications.AnyAsync(
                            x =>
                                x.OrganisationId == document.OrganisationId &&
                                x.Type == NotificationType.DocumentExpiry &&
                                x.RelatedEntityType == "BusinessPartyDocument" &&
                                x.RelatedEntityId == document.Id.ToString() &&
                                !x.IsRead,
                            stoppingToken);

                    if (exists)
                    {
                        continue;
                    }

                    var severity =
                        document.ExpiryDate < today
                            ? NotificationSeverity.Critical
                            : NotificationSeverity.Warning;

                    var title =
                        document.ExpiryDate < today
                            ? "Document expired"
                            : "Document expires soon";

                    var message =
                        document.ExpiryDate < today
                            ? $"{document.Name} expired on {document.ExpiryDate:dd MMM yyyy}"
                            : $"{document.Name} expires on {document.ExpiryDate:dd MMM yyyy}";

                    await notifications.CreateAsync(
                        new CreateNotificationRequest(
                            document.OrganisationId,
                            title,
                            message,
                            NotificationType.DocumentExpiry,
                            severity,
                            "BusinessPartyDocument",
                            document.Id.ToString()),
                        stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Document expiry check failed.");
            }

            await Task.Delay(
                TimeSpan.FromHours(1),
                stoppingToken);
        }
    }
}
