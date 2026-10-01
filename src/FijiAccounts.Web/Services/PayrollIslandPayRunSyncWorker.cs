using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed class PayrollIslandPayRunSyncWorker(IServiceScopeFactory scopes,
    ILogger<PayrollIslandPayRunSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var connections = await db.PayrollIslandConnections.AsNoTracking()
                    .Where(x => x.IsActive && x.AutomaticallySyncAndPostPayRuns)
                    .Select(x => new { x.OrganisationId, UserId = x.UpdatedByUserId })
                    .ToArrayAsync(stoppingToken);
                foreach (var connection in connections)
                {
                    try
                    {
                        using var connectionScope = scopes.CreateScope();
                        var integration = connectionScope.ServiceProvider.GetRequiredService<PayrollIslandIntegrationService>();
                        await integration.SyncAsync(connection.UserId, connection.OrganisationId, stoppingToken);
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        logger.LogWarning(error, "Payroll Island pay-run sync failed for Account Island organisation {OrganisationId}.",
                            connection.OrganisationId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                logger.LogWarning(error, "Payroll Island scheduled pay-run sync could not complete.");
            }
            try { await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
