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
    }}