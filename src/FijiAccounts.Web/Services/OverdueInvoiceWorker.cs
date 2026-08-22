using Microsoft.EntityFrameworkCore;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed class OverdueInvoiceWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<OverdueInvoiceWorker> logger)
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

                var invoices =
                    await db.SalesInvoices
                        .AsNoTracking()
                        .Include(x => x.Customer)
                        .Where(
                            x =>
                                x.DueDate < today &&
                                x.AmountPaid + x.AmountCredited < x.Total &&
                                x.Status != InvoiceStatus.Draft &&
                                x.Status != InvoiceStatus.Paid &&
                                x.Status != InvoiceStatus.Voided &&
                                x.Status != InvoiceStatus.Credited)
                        .ToListAsync(
                            stoppingToken);

                foreach (var invoice in invoices)
                {
                    var exists =
                        await db.Notifications.AnyAsync(
                            x =>
                                x.Type == NotificationType.PaymentOverdue &&
                                x.RelatedEntityId == invoice.Id.ToString() &&
                                x.Status != NotificationStatus.Resolved,
                            stoppingToken);

                    if (exists)
                    {
                        continue;
                    }

                    var daysOverdue =
                        today.DayNumber -
                        invoice.DueDate.DayNumber;

                    var severity =
                        daysOverdue >= 30
                            ? NotificationSeverity.Critical
                            : NotificationSeverity.Warning;

                    await notifications.CreateAsync(
                        new CreateNotificationRequest(
                            invoice.OrganisationId,
                            "Invoice overdue",
                            $"{invoice.InvoiceNumber} for {invoice.Customer.Name} is {daysOverdue} days overdue. Outstanding {invoice.Currency} {(invoice.Total - invoice.AmountPaid - invoice.AmountCredited):N2}.",
                            NotificationType.PaymentOverdue,
                            severity,
                            "SalesInvoice",
                            invoice.Id.ToString()),
                        stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Overdue invoice check failed.");
            }

            await Task.Delay(
                TimeSpan.FromDays(1),
                stoppingToken);
        }
    }
}
