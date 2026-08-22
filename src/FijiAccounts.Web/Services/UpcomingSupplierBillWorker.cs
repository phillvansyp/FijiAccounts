using Microsoft.EntityFrameworkCore;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed class UpcomingSupplierBillWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<UpcomingSupplierBillWorker> logger)
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

                var bills =
                    await db.SupplierBills
                        .AsNoTracking()
                        .Include(x => x.Supplier)
                        .Where(
                            x =>
                                x.DueDate > today &&
                                x.DueDate <= reminderDate &&
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
                                x.Type == NotificationType.PaymentDueSoon &&
                                x.RelatedEntityType == "SupplierBill" &&
                                x.RelatedEntityId == bill.Id.ToString() &&
                                x.Status != NotificationStatus.Resolved,
                            stoppingToken);

                    if (exists)
                    {
                        continue;
                    }

                    var daysUntilDue =
                        bill.DueDate.DayNumber -
                        today.DayNumber;

                    await notifications.CreateAsync(
                        new CreateNotificationRequest(
                            bill.OrganisationId,
                            "Supplier bill due soon",
                            $"{bill.BillNumber} · {bill.Supplier.Name} · due in {daysUntilDue} days.",
                            NotificationType.PaymentDueSoon,
                            NotificationSeverity.Warning,
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
                    "Upcoming supplier bill check failed.");
            }

            await Task.Delay(
                TimeSpan.FromDays(1),
                stoppingToken);
        }
    }
}
