namespace FijiAccounts.Web.Services;

public sealed class PayrollGovernmentPaymentSyncWorker(IServiceScopeFactory scopes,
    ILogger<PayrollGovernmentPaymentSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var sync = scope.ServiceProvider.GetRequiredService<PayrollGovernmentPaymentSyncService>();
                await sync.SyncAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                logger.LogWarning(error, "Payroll Island government payment verification could not complete.");
            }
            try { await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
