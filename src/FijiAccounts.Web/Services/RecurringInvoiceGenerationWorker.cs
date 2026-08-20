using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed class RecurringInvoiceGenerationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<RecurringInvoiceGenerationWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Recurring invoice automation failed.");
            }

            await Task.Delay(
                TimeSpan.FromMinutes(15),
                stoppingToken);
        }
    }

    internal async Task RunOnceAsync(
        CancellationToken ct)
    {
        using var scope =
            scopeFactory.CreateScope();

        var db =
            scope.ServiceProvider
                .GetRequiredService<ApplicationDbContext>();

        var recurring =
            scope.ServiceProvider
                .GetRequiredService<RecurringSalesInvoiceService>();

        await RunOnceCoreAsync(
            db,
            recurring,
            DateTimeOffset.UtcNow,
            ct);
    }

    internal static async Task RunOnceCoreAsync(
        ApplicationDbContext db,
        RecurringSalesInvoiceService recurring,
        DateTimeOffset utcNow,
        CancellationToken ct = default)
    {
        var organisations =
            await db.Organisations
                .AsNoTracking()
                .Where(x =>
                    x.RecurringInvoiceAutomationEnabled)
                .ToListAsync(ct);

        foreach (var organisation in organisations)
        {
            var zone =
                TimeZoneInfo.FindSystemTimeZoneById(
                    organisation.TimeZoneId);

            var localNow =
                TimeZoneInfo.ConvertTime(
                    utcNow,
                    zone);

            if (localNow.TimeOfDay <
                organisation.RecurringInvoiceAutomationTime
                    .ToTimeSpan())
            {
                continue;
            }

            var today =
                DateOnly.FromDateTime(
                    localNow.DateTime);

            var existingRun =
                await db.RecurringInvoiceAutomationRuns
                    .SingleOrDefaultAsync(
                        x =>
                            x.OrganisationId ==
                                organisation.Id &&
                            x.RunDate == today,
                        ct);

            if (existingRun?.Status == "Completed")
            {
                continue;
            }

            var run =
                existingRun ??
                new RecurringInvoiceAutomationRun
                {
                    OrganisationId =
                        organisation.Id,
                    RunDate =
                        today,
                    StartedAtUtc =
                        utcNow,
                    Status =
                        "Running"
                };

            if (existingRun is null)
            {
                db.RecurringInvoiceAutomationRuns.Add(run);
            }
            else
            {
                run.StartedAtUtc =
                    utcNow;

                run.Status =
                    "Running";

                run.ErrorMessage =
                    null;

                run.CompletedAtUtc =
                    null;
            }

            await db.SaveChangesAsync(ct);

            try
            {
                var generated =
                    await recurring.GenerateDueAutomaticallyAsync(
                        organisation.Id,
                        today,
                        ct);

                run.GeneratedCount =
                    generated.Count;

                run.Status =
                    "Completed";

                run.CompletedAtUtc =
                    utcNow;
            }
            catch (Exception ex)
            {
                run.Status =
                    "Failed";

                run.ErrorMessage =
                    ex.Message;

                run.CompletedAtUtc =
                    utcNow;
            }

            await db.SaveChangesAsync(ct);
        }
    }
}