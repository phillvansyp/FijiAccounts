using Microsoft.EntityFrameworkCore;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Xunit;

namespace FijiAccounts.Web.Tests;

public sealed class RecurringInvoiceGenerationWorkerTests
{
    [Fact]
    public async Task RunOnceCoreAsync_AtSixAmLocal_GeneratesInvoice()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        test.Organisation.TimeZoneId =
            "Pacific/Fiji";

        test.Organisation.RecurringInvoiceAutomationEnabled =
            true;

        test.Organisation.RecurringInvoiceAutomationTime =
            new TimeOnly(6, 0);

        await test.Db.SaveChangesAsync();

        var recurring =
            new RecurringSalesInvoiceService(
                test.Db,
                test.Access,
                test.SalesInvoices);

        await recurring.CreateAsync(
            test.UserId,
            new(
                test.Organisation.Id,
                test.Customer.Id,
                RecurringSalesInvoiceFrequency.Monthly,
                new DateOnly(2026, 8, 1),
                14,
                [
                    new(
                        "Automation test",
                        1m,
                        100m,
                        VatTreatment.Standard,
                        test.Account("4000").Id)
                ]));

        var utcAtSix =
            new DateTimeOffset(
                2026,
                8,
                20,
                18,
                0,
                0,
                TimeSpan.Zero);

        await RecurringInvoiceGenerationWorker.RunOnceCoreAsync(
            test.Db,
            recurring,
            utcAtSix);

        var run =
            await test.Db.RecurringInvoiceAutomationRuns
                .SingleAsync();

        Assert.True(
            run.Status == "Completed",
            $"Automation failed. Status: {run.Status}. Error: {run.ErrorMessage}");

        Assert.Equal(
            1,
            run.GeneratedCount);

        Assert.Single(
            await test.Db.SalesInvoices
                .Where(x =>
                    x.OrganisationId ==
                        test.Organisation.Id)
                .ToListAsync());
        var generation = await test.Db.RecurringSalesInvoiceGenerations
            .AsNoTracking()
            .SingleAsync();
        var generationAudit = await test.Db.AuditEvents
            .AsNoTracking()
            .SingleAsync(x =>
                x.EventType == "RecurringSalesInvoiceGenerated" &&
                x.EntityId == generation.RecurringSalesInvoiceId.ToString());
        Assert.Equal("system", generation.GeneratedByUserId);
        Assert.Equal("system", generationAudit.UserId);
    }

    [Fact]
    public async Task RunOnceCoreAsync_BeforeSixAmLocal_DoesNotRun()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        test.Organisation.TimeZoneId =
            "Pacific/Fiji";

        test.Organisation.RecurringInvoiceAutomationEnabled =
            true;

        test.Organisation.RecurringInvoiceAutomationTime =
            new TimeOnly(6, 0);

        await test.Db.SaveChangesAsync();

        var recurring =
            new RecurringSalesInvoiceService(
                test.Db,
                test.Access,
                test.SalesInvoices);

        var utcBeforeSix =
            new DateTimeOffset(
                2026,
                8,
                20,
                17,
                59,
                0,
                TimeSpan.Zero);

        await RecurringInvoiceGenerationWorker.RunOnceCoreAsync(
            test.Db,
            recurring,
            utcBeforeSix);

        Assert.Empty(
            await test.Db.RecurringInvoiceAutomationRuns
                .ToListAsync());

        Assert.Empty(
            await test.Db.SalesInvoices
                .Where(x =>
                    x.OrganisationId ==
                        test.Organisation.Id)
                .ToListAsync());
    }

    [Fact]
    public async Task RunOnceCoreAsync_WhenCompletedRunExists_SkipsDuplicateGeneration()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        test.Organisation.TimeZoneId =
            "Pacific/Fiji";

        test.Organisation.RecurringInvoiceAutomationEnabled =
            true;

        test.Organisation.RecurringInvoiceAutomationTime =
            new TimeOnly(6, 0);

        await test.Db.SaveChangesAsync();

        test.Db.RecurringInvoiceAutomationRuns.Add(
            new RecurringInvoiceAutomationRun
            {
                OrganisationId = test.Organisation.Id,
                RunDate = new DateOnly(2026, 8, 21),
                StartedAtUtc = DateTimeOffset.UtcNow,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Status = "Completed",
                GeneratedCount = 1
            });

        await test.Db.SaveChangesAsync();

        var recurring =
            new RecurringSalesInvoiceService(
                test.Db,
                test.Access,
                test.SalesInvoices);

        var utcAtSix =
            new DateTimeOffset(
                2026,
                8,
                20,
                18,
                0,
                0,
                TimeSpan.Zero);

        await RecurringInvoiceGenerationWorker.RunOnceCoreAsync(
            test.Db,
            recurring,
            utcAtSix);

        Assert.Single(
            await test.Db.RecurringInvoiceAutomationRuns
                .ToListAsync());

        Assert.Empty(
            await test.Db.SalesInvoices
                .Where(x =>
                    x.OrganisationId ==
                        test.Organisation.Id)
                .ToListAsync());
    }

    [Fact]
    public async Task RunOnceCoreAsync_WhenFailedRunExists_RetriesSuccessfully()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        test.Organisation.TimeZoneId =
            "Pacific/Fiji";

        test.Organisation.RecurringInvoiceAutomationEnabled =
            true;

        test.Organisation.RecurringInvoiceAutomationTime =
            new TimeOnly(6, 0);

        await test.Db.SaveChangesAsync();

        test.Db.RecurringInvoiceAutomationRuns.Add(
            new RecurringInvoiceAutomationRun
            {
                OrganisationId = test.Organisation.Id,
                RunDate = new DateOnly(2026, 8, 21),
                StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Status = "Failed",
                ErrorMessage = "Previous failure"
            });

        await test.Db.SaveChangesAsync();

        var recurring =
            new RecurringSalesInvoiceService(
                test.Db,
                test.Access,
                test.SalesInvoices);

        var utcAtSix =
            new DateTimeOffset(
                2026,
                8,
                20,
                18,
                0,
                0,
                TimeSpan.Zero);

        await RecurringInvoiceGenerationWorker.RunOnceCoreAsync(
            test.Db,
            recurring,
            utcAtSix);

        var run =
            await test.Db.RecurringInvoiceAutomationRuns
                .SingleAsync();

        Assert.Equal(
            "Completed",
            run.Status);

        Assert.Null(
            run.ErrorMessage);
    }

    [Fact]
    public async Task RunOnceCoreAsync_WhenRecentRunIsActive_SkipsConcurrentAttempt()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        await CreateRecurringAsync(test, test.Organisation.Id, test.Customer.Id);
        var utcAtSix = new DateTimeOffset(
            2026,
            8,
            20,
            18,
            0,
            0,
            TimeSpan.Zero);
        var run = new RecurringInvoiceAutomationRun
        {
            OrganisationId = test.Organisation.Id,
            RunDate = new DateOnly(2026, 8, 21),
            StartedAtUtc = utcAtSix.AddMinutes(-10),
            Status = "Running"
        };
        test.Db.RecurringInvoiceAutomationRuns.Add(run);
        await test.Db.SaveChangesAsync();
        var recurring = new RecurringSalesInvoiceService(
            test.Db,
            test.Access,
            test.SalesInvoices);

        await RecurringInvoiceGenerationWorker.RunOnceCoreAsync(
            test.Db,
            recurring,
            utcAtSix);

        Assert.Equal("Running", run.Status);
        Assert.Empty(await test.Db.SalesInvoices.AsNoTracking().ToListAsync());
        Assert.DoesNotContain(
            await test.Db.AuditEvents.AsNoTracking().ToListAsync(),
            x => x.EventType == "RecurringSalesInvoiceGenerated");
    }

    [Fact]
    public async Task RunOnceCoreAsync_WhenRunningLeaseIsStale_Retries()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        await CreateRecurringAsync(test, test.Organisation.Id, test.Customer.Id);
        var utcAtSix = new DateTimeOffset(
            2026,
            8,
            20,
            18,
            0,
            0,
            TimeSpan.Zero);
        var run = new RecurringInvoiceAutomationRun
        {
            OrganisationId = test.Organisation.Id,
            RunDate = new DateOnly(2026, 8, 21),
            StartedAtUtc = utcAtSix.AddHours(-2),
            Status = "Running"
        };
        test.Db.RecurringInvoiceAutomationRuns.Add(run);
        await test.Db.SaveChangesAsync();
        var recurring = new RecurringSalesInvoiceService(
            test.Db,
            test.Access,
            test.SalesInvoices);

        await RecurringInvoiceGenerationWorker.RunOnceCoreAsync(
            test.Db,
            recurring,
            utcAtSix);

        Assert.Equal("Completed", run.Status);
        Assert.Equal(utcAtSix, run.StartedAtUtc);
        Assert.Single(await test.Db.SalesInvoices.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task RunOnceCoreAsync_IsolatesTenantFailureAndContinuesLaterOrganisations()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.CreatedAt = new DateTimeOffset(
            2026,
            1,
            1,
            0,
            0,
            0,
            TimeSpan.Zero);
        test.Organisation.TimeZoneId = "Invalid/AutomationZone";
        var structures = new EnterpriseStructureService(test.Db);
        var second = await structures.AddCompanyAsync(
            test.UserId,
            new CreateGroupCompanyRequest(
                test.Organisation.Id,
                "Second Automation Limited",
                null,
                null,
                "FJ",
                OrganisationKind.Business));
        second.CreatedAt = test.Organisation.CreatedAt.AddDays(1);
        var customer = new BusinessParty
        {
            OrganisationId = second.Id,
            Name = "Second Automation Customer",
            Type = PartyType.Customer,
            IsActive = true
        };
        test.Db.BusinessParties.Add(customer);
        await test.Db.SaveChangesAsync();
        await CreateRecurringAsync(test, second.Id, customer.Id);
        var recurring = new RecurringSalesInvoiceService(
            test.Db,
            test.Access,
            test.SalesInvoices);
        var utcAtSix = new DateTimeOffset(
            2026,
            8,
            20,
            18,
            0,
            0,
            TimeSpan.Zero);

        await RecurringInvoiceGenerationWorker.RunOnceCoreAsync(
            test.Db,
            recurring,
            utcAtSix);

        var runs = await test.Db.RecurringInvoiceAutomationRuns
            .AsNoTracking()
            .OrderBy(x => x.OrganisationId)
            .ToListAsync();
        Assert.Equal(2, runs.Count);
        var failed = runs.Single(x => x.OrganisationId == test.Organisation.Id);
        Assert.Equal("Failed", failed.Status);
        Assert.NotNull(failed.ErrorMessage);
        Assert.InRange(failed.ErrorMessage!.Length, 1, 1000);
        Assert.Equal(
            "Completed",
            runs.Single(x => x.OrganisationId == second.Id).Status);
        Assert.Single(
            await test.Db.SalesInvoices
                .AsNoTracking()
                .Where(x => x.OrganisationId == second.Id)
                .ToListAsync());
    }

    private static async Task CreateRecurringAsync(
        AccountingTestDatabase test,
        Guid organisationId,
        Guid customerId)
    {
        var revenueAccountId = await test.Db.LedgerAccounts
            .Where(x => x.OrganisationId == organisationId && x.Code == "4000")
            .Select(x => x.Id)
            .SingleAsync();
        var recurring = new RecurringSalesInvoiceService(
            test.Db,
            test.Access,
            test.SalesInvoices);
        await recurring.CreateAsync(
            test.UserId,
            new(
                organisationId,
                customerId,
                RecurringSalesInvoiceFrequency.Monthly,
                new DateOnly(2026, 8, 1),
                14,
                [
                    new(
                        "Automation helper",
                        1m,
                        100m,
                        VatTreatment.Standard,
                        revenueAccountId)
                ]));
    }
}
