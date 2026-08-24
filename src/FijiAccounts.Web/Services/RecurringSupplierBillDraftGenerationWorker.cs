using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed class RecurringSupplierBillDraftGenerationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<RecurringSupplierBillDraftGenerationWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Recurring supplier bill draft generation failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var recurring = scope.ServiceProvider
            .GetRequiredService<RecurringSupplierBillService>();

        await RunOnceCoreAsync(
            db,
            recurring,
            DateTimeOffset.UtcNow,
            logger,
            ct);
    }

    internal static async Task<int> RunOnceCoreAsync(
        ApplicationDbContext db,
        RecurringSupplierBillService recurring,
        DateTimeOffset utcNow,
        ILogger logger,
        CancellationToken ct = default)
    {
        var organisationIds = await db.RecurringSupplierBills
            .AsNoTracking()
            .Where(x =>
                x.IsActive &&
                x.Status == RecurringSupplierBillStatus.Active)
            .Select(x => x.OrganisationId)
            .Distinct()
            .ToListAsync(ct);
        var organisations = (await db.Organisations
            .AsNoTracking()
            .Where(x => organisationIds.Contains(x.Id))
            .ToListAsync(ct))
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .ToList();
        var generatedCount = 0;

        foreach (var organisation in organisations)
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(
                    organisation.TimeZoneId);
                var localNow = TimeZoneInfo.ConvertTime(utcNow, zone);
                var throughDate = DateOnly.FromDateTime(localNow.DateTime);
                var generated = await recurring.GenerateDueDraftsAutomaticallyAsync(
                    organisation.Id,
                    throughDate,
                    ct);
                generatedCount += generated.Count;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Could not generate recurring supplier bill drafts for organisation {OrganisationId}.",
                    organisation.Id);
            }
        }

        return generatedCount;
    }
}
