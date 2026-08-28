using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed class VatTurnoverMonitorWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<VatTurnoverMonitorWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var monitor = scope.ServiceProvider.GetRequiredService<VatTurnoverMonitorService>();
                var organisationIds = await db.Organisations
                    .AsNoTracking()
                    .Where(x => x.CountryCode == "FJ")
                    .Select(x => x.Id)
                    .ToListAsync(stoppingToken);
                var today = DateOnly.FromDateTime(DateTime.Today);

                foreach (var organisationId in organisationIds)
                {
                    await monitor.RefreshAlertAsync(organisationId, today, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "VAT turnover monitoring failed.");
            }

            await Task.Delay(TimeSpan.FromHours(12), stoppingToken);
        }
    }
}
