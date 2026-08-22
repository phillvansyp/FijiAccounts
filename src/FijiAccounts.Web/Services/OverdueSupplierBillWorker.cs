using Microsoft.EntityFrameworkCore;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed class OverdueSupplierBillWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<OverdueSupplierBillWorker> logger)
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

                var bills =
                    await db.SupplierBills
                        .AsNoTracking()
                        .Include(x => x.Supplier)
                        .Where(
                            x =>
                                x.DueDate < today &&
                                x.AmountPaid + x.AmountCredited < x.Total &&
                                x.Status != BillStatus.Paid &&
                                x.Status != BillStatus.Voided &&
                                x.Status != BillStatus.Credited)
                        .ToListAsync(
                            stoppingToken);

                foreach (var bill in bills)
                {
                    var exists =
                        await db.Notifications.AnyAsync(
                            x =>
                                x.Type == NotificationType.PaymentOverdue &&
                                x.RelatedEntityType == "SupplierBill" &&
                                x.RelatedEntityId == bill.Id.ToString() &&
                                x.Status != NotificationStatus.Resolved,
                            stoppingToken);

                    if (exists)
                    {
                        continue;
                    }

                    var daysOverdue =
                        today.DayNumber -
                        bill.DueDate.DayNumber;

                    var severity =
                        daysOverdue >= 30
                            ? NotificationSeverity.Critical
                            : NotificationSeverity.Warning;

                    await notifications.CreateAsync(
                        new CreateNotificationRequest(
                            bill.OrganisationId,
                            "Supplier bill overdue",
                            $"{bill.BillNumber} for {bill.Supplier.Name} is {daysOverdue} days overdue.",
                            NotificationType.PaymentOverdue,
                            severity,
                            "SupplierBill",
                            bill.Id.ToString(),
                            bill.Total - bill.AmountPaid - bill.AmountCredited,
                            bill.Currency),
                        stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Overdue supplier bill check failed.");
            }

            await Task.Delay(
                TimeSpan.FromDays(1),
                stoppingToken);
        }
    }
}
