using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed class ImmutableDocumentIntegrityWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ImmutableDocumentIntegrityWorker> logger)
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
                var integrity = scope.ServiceProvider
                    .GetRequiredService<ImmutableDocumentIntegrityService>();
                var recentCutoffTicks = DateTimeOffset.UtcNow
                    .AddHours(-24)
                    .UtcTicks;
                var organisationIds = await db.Organisations
                    .AsNoTracking()
                    .Where(x =>
                        (x.CountryCode == "FJ" || x.CountryCode == "NZ") &&
                        !db.ImmutableDocumentIntegrityScans.Any(scan =>
                            scan.OrganisationId == x.Id &&
                            scan.CompletedAtTicks >= recentCutoffTicks))
                    .Select(x => x.Id)
                    .ToListAsync(stoppingToken);
                foreach (var organisationId in organisationIds)
                {
                    await integrity.ScanSystemAsync(organisationId, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Immutable document integrity scanning failed.");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
