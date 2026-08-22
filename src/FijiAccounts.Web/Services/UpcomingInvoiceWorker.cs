using Microsoft.EntityFrameworkCore;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed class UpcomingInvoiceWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<UpcomingInvoiceWorker> logger)
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

                var reminderDate =
                    today.AddDays(7);

                var invoices =
                    await db.SalesInvoices
                        .AsNoTracking()
                        .Include(x => x.Customer)
                        .Where(
                            x =>
                                x.DueDate > today &&
                                x.DueDate <= reminderDate &&
                                x.AmountPaid + x.AmountCredited < x.Total &&
                                x.Status != InvoiceStatus.Paid &&
                                x.Status != InvoiceStatus.Voided &&
                                x.Status != InvoiceStatus.Credited &&
                                x.Status != InvoiceStatus.Draft)
                        .ToListAsync(
                            stoppingToken);

                foreach (var invoice in invoices)
                {
                    var exists =
                        await db.Notifications.AnyAsync(
                            x =>
                                x.Type == NotificationType.PaymentDueSoon &&
                                x.RelatedEntityType == "SalesInvoice" &&
                                x.RelatedEntityId == invoice.Id.ToString() &&
                                x.Status != NotificationStatus.Resolved,
                            stoppingToken);

                    if (exists)
                    {
                        continue;
                    }

                    var daysUntilDue =
                        invoice.DueDate.DayNumber -
                        today.DayNumber;

                    await notifications.CreateAsync(
                        new CreateNotificationRequest(
                            invoice.OrganisationId,
                            "Invoice due soon",
                            $"{invoice.InvoiceNumber} · {invoice.Customer.Name} · due in {daysUntilDue} days.",
                            NotificationType.PaymentDueSoon,
                            NotificationSeverity.Warning,
                            "SalesInvoice",
                            invoice.Id.ToString(),
                            invoice.Total - invoice.AmountPaid - invoice.AmountCredited,
                            invoice.Currency),
                        stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Upcoming invoice check failed.");
            }

            await Task.Delay(
                TimeSpan.FromHours(1),
                stoppingToken);
        }
    }
}
