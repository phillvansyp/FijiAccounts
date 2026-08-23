using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed class RecurringInvoiceGenerationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<RecurringInvoiceGenerationWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan ActiveRunLease =
        TimeSpan.FromHours(1);

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
            (await db.Organisations
                    .AsNoTracking()
                    .Where(x =>
                        x.RecurringInvoiceAutomationEnabled)
                    .ToListAsync(ct))
                .OrderBy(x => x.CreatedAt)
                .ThenBy(x => x.Id)
                .ToList();

        foreach (var organisation in organisations)
        {
            var runDate =
                DateOnly.FromDateTime(utcNow.UtcDateTime);

            try
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

                runDate =
                    DateOnly.FromDateTime(
                        localNow.DateTime);

                await RunOrganisationAsync(
                    db,
                    recurring,
                    organisation.Id,
                    runDate,
                    utcNow,
                    ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await RecordFailureAsync(
                    db,
                    organisation.Id,
                    runDate,
                    utcNow,
                    ex,
                    ct);
            }
        }
    }

    private static async Task RunOrganisationAsync(
        ApplicationDbContext db,
        RecurringSalesInvoiceService recurring,
        Guid organisationId,
        DateOnly runDate,
        DateTimeOffset utcNow,
        CancellationToken ct)
    {
        var existingRun =
            await db.RecurringInvoiceAutomationRuns
                .SingleOrDefaultAsync(
                    x =>
                        x.OrganisationId == organisationId &&
                        x.RunDate == runDate,
                    ct);

        if (existingRun?.Status == "Completed" ||
            existingRun?.Status == "Running" &&
            utcNow - existingRun.StartedAtUtc < ActiveRunLease)
        {
            return;
        }

        var run =
            existingRun ??
            new RecurringInvoiceAutomationRun
            {
                OrganisationId = organisationId,
                RunDate = runDate,
                StartedAtUtc = utcNow,
                Status = "Running"
            };

        if (existingRun is null)
        {
            db.RecurringInvoiceAutomationRuns.Add(run);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                if (await db.RecurringInvoiceAutomationRuns.AnyAsync(
                        x =>
                            x.OrganisationId == organisationId &&
                            x.RunDate == runDate,
                        ct))
                {
                    return;
                }

                throw;
            }
        }
        else
        {
            run.StartedAtUtc = utcNow;
            run.Status = "Running";
            run.ErrorMessage = null;
            run.CompletedAtUtc = null;
            run.GeneratedCount = 0;

            await db.SaveChangesAsync(ct);
        }

        var generated =
            await recurring.GenerateDueAutomaticallyAsync(
                organisationId,
                runDate,
                ct);

        run.GeneratedCount = generated.Count;
        run.Status = "Completed";
        run.CompletedAtUtc = utcNow;

        await db.SaveChangesAsync(ct);
    }

    private static async Task RecordFailureAsync(
        ApplicationDbContext db,
        Guid organisationId,
        DateOnly runDate,
        DateTimeOffset startedAtUtc,
        Exception exception,
        CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var run =
            await db.RecurringInvoiceAutomationRuns
                .SingleOrDefaultAsync(
                    x =>
                        x.OrganisationId == organisationId &&
                        x.RunDate == runDate,
                    ct);

        if (run?.Status == "Completed" ||
            run?.Status == "Running" &&
            run.StartedAtUtc != startedAtUtc)
        {
            return;
        }

        if (run is null)
        {
            run = new RecurringInvoiceAutomationRun
            {
                OrganisationId = organisationId,
                RunDate = runDate,
                StartedAtUtc = startedAtUtc,
                Status = "Failed"
            };
            db.RecurringInvoiceAutomationRuns.Add(run);
        }

        run.Status = "Failed";
        run.ErrorMessage = NormaliseError(exception);
        run.CompletedAtUtc = startedAtUtc;
        await db.SaveChangesAsync(ct);
    }

    private static string NormaliseError(Exception exception)
    {
        var message = string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message.Trim();

        return message.Length <= 1000
            ? message
            : message[..1000];
    }
}
