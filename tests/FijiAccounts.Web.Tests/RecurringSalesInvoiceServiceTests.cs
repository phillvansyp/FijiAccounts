using Microsoft.EntityFrameworkCore;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class RecurringSalesInvoiceServiceTests
{
    [Fact]
    public async Task CreateAsync_CreatesActiveTemplateWithLines()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new RecurringSalesInvoiceService(
                test.Db,
                test.Access,
                test.SalesInvoices);

        var recurring =
            await service.CreateAsync(
                test.UserId,
                new RecurringSalesInvoiceRequest(
                    OrganisationId: test.Organisation.Id,
                    CustomerId: test.Customer.Id,
                    Frequency: RecurringSalesInvoiceFrequency.Monthly,
                    StartDate: new DateOnly(2027, 1, 31),
                    DueDays: 14,
                    Lines:
                    [
                        new RecurringSalesInvoiceLineRequest(
                            Description: "Monthly service",
                            Quantity: 1m,
                            UnitPrice: 250m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id)
                    ]));

        var reloaded =
            await test.Db.RecurringSalesInvoices
                .AsNoTracking()
                .Include(x => x.Lines)
                .SingleAsync(x => x.Id == recurring.Id);

        Assert.Equal(
            test.Customer.Id,
            reloaded.CustomerId);

        Assert.Equal(
            RecurringSalesInvoiceFrequency.Monthly,
            reloaded.Frequency);

        Assert.Equal(
            new DateOnly(2027, 1, 31),
            reloaded.StartDate);

        Assert.Equal(
            new DateOnly(2027, 1, 31),
            reloaded.NextInvoiceDate);

        Assert.Equal(
            14,
            reloaded.DueDays);

        Assert.True(reloaded.IsActive);

        Assert.Equal(
            RecurringSalesInvoiceStatus.Active,
            reloaded.Status);

        var line =
            Assert.Single(reloaded.Lines);

        Assert.Equal(
            "Monthly service",
            line.Description);

        Assert.Equal(
            250m,
            line.UnitPrice);

        Assert.Equal(
            test.Account("4000").Id,
            line.RevenueAccountId);
    }

    [Fact]
    public async Task CreateAsync_WhenLineIsInvalid_IsRejectedWithoutTemplate()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new RecurringSalesInvoiceService(
                test.Db,
                test.Access,
                test.SalesInvoices);

        var countBefore =
            await test.Db.RecurringSalesInvoices.CountAsync();

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new RecurringSalesInvoiceRequest(
                            OrganisationId: test.Organisation.Id,
                            CustomerId: test.Customer.Id,
                            Frequency: RecurringSalesInvoiceFrequency.Monthly,
                            StartDate: new DateOnly(2027, 1, 31),
                            DueDays: 14,
                            Lines:
                            [
                                new RecurringSalesInvoiceLineRequest(
                                    Description: "",
                                    Quantity: 0m,
                                    UnitPrice: -1m,
                                    VatTreatment: VatTreatment.Standard,
                                    RevenueAccountId: test.Account("4000").Id)
                            ])));

        Assert.Contains(
            "description",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            countBefore,
            await test.Db.RecurringSalesInvoices.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_WhenRevenueAccountIsInvalid_IsRejectedWithoutTemplate()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new RecurringSalesInvoiceService(
                test.Db,
                test.Access,
                test.SalesInvoices);

        var countBefore =
            await test.Db.RecurringSalesInvoices.CountAsync();

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new RecurringSalesInvoiceRequest(
                            OrganisationId: test.Organisation.Id,
                            CustomerId: test.Customer.Id,
                            Frequency: RecurringSalesInvoiceFrequency.Monthly,
                            StartDate: new DateOnly(2027, 1, 31),
                            DueDays: 14,
                            Lines:
                            [
                                new RecurringSalesInvoiceLineRequest(
                                    Description: "Invalid revenue",
                                    Quantity: 1m,
                                    UnitPrice: 100m,
                                    VatTreatment: VatTreatment.Standard,
                                    RevenueAccountId: test.Account("6500").Id)
                            ])));

        Assert.Contains(
            "revenue account",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            countBefore,
            await test.Db.RecurringSalesInvoices.CountAsync());
    }

    [Fact]
    public async Task GenerateDueAsync_CreatesPostedSalesInvoiceAndAdvancesSchedule()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new RecurringSalesInvoiceService(
                test.Db,
                test.Access,
                test.SalesInvoices);

        var recurring =
            await service.CreateAsync(
                test.UserId,
                new RecurringSalesInvoiceRequest(
                    OrganisationId: test.Organisation.Id,
                    CustomerId: test.Customer.Id,
                    Frequency: RecurringSalesInvoiceFrequency.Monthly,
                    StartDate: new DateOnly(2027, 1, 15),
                    DueDays: 14,
                    Lines:
                    [
                        new RecurringSalesInvoiceLineRequest(
                            Description: "Monthly service",
                            Quantity: 1m,
                            UnitPrice: 250m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id)
                    ]));

        var generated =
            await service.GenerateDueAsync(
                test.UserId,
                test.Organisation.Id,
                new DateOnly(2027, 1, 15));

        var invoice =
            Assert.Single(generated);

        Assert.Equal(
            InvoiceStatus.Posted,
            invoice.Status);

        Assert.Equal(
            new DateOnly(2027, 1, 15),
            invoice.IssueDate);

        Assert.Equal(
            new DateOnly(2027, 1, 29),
            invoice.DueDate);

        Assert.Equal(
            test.Customer.Id,
            invoice.CustomerId);

        var generation =
            await test.Db.RecurringSalesInvoiceGenerations
                .AsNoTracking()
                .SingleAsync(x =>
                    x.RecurringSalesInvoiceId == recurring.Id);

        Assert.Equal(
            invoice.Id,
            generation.SalesInvoiceId);

        Assert.Equal(
            new DateOnly(2027, 1, 15),
            generation.ScheduledDate);

        var reloaded =
            await test.Db.RecurringSalesInvoices
                .AsNoTracking()
                .SingleAsync(x => x.Id == recurring.Id);

        Assert.Equal(
            new DateOnly(2027, 2, 15),
            reloaded.NextInvoiceDate);
    }

    [Fact]
    public async Task GenerateDueAsync_CalledTwice_DoesNotDuplicateOccurrence()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new RecurringSalesInvoiceService(
                test.Db,
                test.Access,
                test.SalesInvoices);

        var recurring =
            await service.CreateAsync(
                test.UserId,
                new RecurringSalesInvoiceRequest(
                    OrganisationId: test.Organisation.Id,
                    CustomerId: test.Customer.Id,
                    Frequency: RecurringSalesInvoiceFrequency.Monthly,
                    StartDate: new DateOnly(2027, 2, 1),
                    DueDays: 7,
                    Lines:
                    [
                        new RecurringSalesInvoiceLineRequest(
                            Description: "Subscription",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id)
                    ]));

        var first =
            await service.GenerateDueAsync(
                test.UserId,
                test.Organisation.Id,
                new DateOnly(2027, 2, 1));

        await test.Db.RecurringSalesInvoices
            .Where(x => x.Id == recurring.Id)
            .ExecuteUpdateAsync(update =>
                update.SetProperty(
                    x => x.NextInvoiceDate,
                    new DateOnly(2027, 2, 1)));
        test.Db.ChangeTracker.Clear();

        var second =
            await service.GenerateDueAsync(
                test.UserId,
                test.Organisation.Id,
                new DateOnly(2027, 2, 1));

        Assert.Single(first);
        Assert.Empty(second);

        Assert.Single(
            await test.Db.RecurringSalesInvoiceGenerations
                .AsNoTracking()
                .Where(x =>
                    x.RecurringSalesInvoiceId == recurring.Id)
                .ToListAsync());
        Assert.Equal(
            new DateOnly(2027, 3, 1),
            await test.Db.RecurringSalesInvoices
                .Where(x => x.Id == recurring.Id)
                .Select(x => x.NextInvoiceDate)
                .SingleAsync());
    }

    [Fact]
    public async Task GenerateDueAsync_ThroughLaterDate_CatchesUpOccurrences()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new RecurringSalesInvoiceService(
                test.Db,
                test.Access,
                test.SalesInvoices);

        var recurring =
            await service.CreateAsync(
                test.UserId,
                new RecurringSalesInvoiceRequest(
                    OrganisationId: test.Organisation.Id,
                    CustomerId: test.Customer.Id,
                    Frequency: RecurringSalesInvoiceFrequency.Monthly,
                    StartDate: new DateOnly(2027, 1, 1),
                    DueDays: 14,
                    Lines:
                    [
                        new RecurringSalesInvoiceLineRequest(
                            Description: "Recurring service",
                            Quantity: 1m,
                            UnitPrice: 75m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id)
                    ]));

        var generated =
            await service.GenerateDueAsync(
                test.UserId,
                test.Organisation.Id,
                new DateOnly(2027, 3, 1));

        Assert.Equal(
            3,
            generated.Count);

        var dates =
            await test.Db.RecurringSalesInvoiceGenerations
                .AsNoTracking()
                .Where(x =>
                    x.RecurringSalesInvoiceId == recurring.Id)
                .OrderBy(x => x.ScheduledDate)
                .Select(x => x.ScheduledDate)
                .ToListAsync();

        Assert.Equal(
            [
                new DateOnly(2027, 1, 1),
                new DateOnly(2027, 2, 1),
                new DateOnly(2027, 3, 1)
            ],
            dates);
    }

    [Fact]
    public async Task GenerateDueAsync_MonthlyOnThirtyFirst_ReturnsToThirtyFirstAfterShortMonth()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new RecurringSalesInvoiceService(
                test.Db,
                test.Access,
                test.SalesInvoices);

        var recurring =
            await service.CreateAsync(
                test.UserId,
                new RecurringSalesInvoiceRequest(
                    OrganisationId: test.Organisation.Id,
                    CustomerId: test.Customer.Id,
                    Frequency: RecurringSalesInvoiceFrequency.Monthly,
                    StartDate: new DateOnly(2027, 1, 31),
                    DueDays: 14,
                    Lines:
                    [
                        new RecurringSalesInvoiceLineRequest(
                            Description: "Month end service",
                            Quantity: 1m,
                            UnitPrice: 150m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id)
                    ]));

        var generated =
            await service.GenerateDueAsync(
                test.UserId,
                test.Organisation.Id,
                new DateOnly(2027, 3, 31));

        Assert.Equal(
            3,
            generated.Count);

        var dates =
            await test.Db.RecurringSalesInvoiceGenerations
                .AsNoTracking()
                .Where(x =>
                    x.RecurringSalesInvoiceId == recurring.Id)
                .OrderBy(x => x.ScheduledDate)
                .Select(x => x.ScheduledDate)
                .ToListAsync();

        Assert.Equal(
            [
                new DateOnly(2027, 1, 31),
                new DateOnly(2027, 2, 28),
                new DateOnly(2027, 3, 31)
            ],
            dates);

        var reloaded =
            await test.Db.RecurringSalesInvoices
                .AsNoTracking()
                .SingleAsync(x => x.Id == recurring.Id);

        Assert.Equal(
            new DateOnly(2027, 4, 30),
            reloaded.NextInvoiceDate);
    }


    [Fact]
    public async Task SetActiveAsync_PauseStopsGenerationAndResumeRestartsIt()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new RecurringSalesInvoiceService(
                test.Db,
                test.Access,
                test.SalesInvoices);

        var recurring =
            await service.CreateAsync(
                test.UserId,
                new RecurringSalesInvoiceRequest(
                    OrganisationId: test.Organisation.Id,
                    CustomerId: test.Customer.Id,
                    Frequency: RecurringSalesInvoiceFrequency.Monthly,
                    StartDate: new DateOnly(2027, 4, 1),
                    DueDays: 14,
                    Lines:
                    [
                        new RecurringSalesInvoiceLineRequest(
                            Description: "Paused service",
                            Quantity: 1m,
                            UnitPrice: 120m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id)
                    ]));

        await service.SetActiveAsync(
            test.UserId,
            test.Organisation.Id,
            recurring.Id,
            false);

        var paused =
            await test.Db.RecurringSalesInvoices
                .AsNoTracking()
                .SingleAsync(x => x.Id == recurring.Id);

        Assert.False(paused.IsActive);

        Assert.Equal(
            RecurringSalesInvoiceStatus.Paused,
            paused.Status);

        var whilePaused =
            await service.GenerateDueAsync(
                test.UserId,
                test.Organisation.Id,
                new DateOnly(2027, 4, 1));

        Assert.Empty(whilePaused);

        await service.SetActiveAsync(
            test.UserId,
            test.Organisation.Id,
            recurring.Id,
            true);

        var resumed =
            await test.Db.RecurringSalesInvoices
                .AsNoTracking()
                .SingleAsync(x => x.Id == recurring.Id);

        Assert.True(resumed.IsActive);

        Assert.Equal(
            RecurringSalesInvoiceStatus.Active,
            resumed.Status);

        var generated =
            await service.GenerateDueAsync(
                test.UserId,
                test.Organisation.Id,
                new DateOnly(2027, 4, 1));

        Assert.Single(generated);

        Assert.Single(
            await test.Db.RecurringSalesInvoiceGenerations
                .AsNoTracking()
                .Where(x =>
                    x.RecurringSalesInvoiceId == recurring.Id)
                .ToListAsync());
    }


    [Fact]
    public async Task EndAsync_StopsFutureGenerationAndCannotBeResumed()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new RecurringSalesInvoiceService(
                test.Db,
                test.Access,
                test.SalesInvoices);

        var recurring =
            await service.CreateAsync(
                test.UserId,
                new RecurringSalesInvoiceRequest(
                    OrganisationId: test.Organisation.Id,
                    CustomerId: test.Customer.Id,
                    Frequency: RecurringSalesInvoiceFrequency.Monthly,
                    StartDate: new DateOnly(2027, 5, 1),
                    DueDays: 14,
                    Lines:
                    [
                        new RecurringSalesInvoiceLineRequest(
                            Description: "Ended service",
                            Quantity: 1m,
                            UnitPrice: 175m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id)
                    ]));

        await service.EndAsync(
            test.UserId,
            test.Organisation.Id,
            recurring.Id);

        var ended =
            await test.Db.RecurringSalesInvoices
                .AsNoTracking()
                .SingleAsync(x => x.Id == recurring.Id);

        Assert.False(ended.IsActive);

        Assert.Equal(
            RecurringSalesInvoiceStatus.Ended,
            ended.Status);

        var generated =
            await service.GenerateDueAsync(
                test.UserId,
                test.Organisation.Id,
                new DateOnly(2027, 12, 1));

        Assert.Empty(generated);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.SetActiveAsync(
                        test.UserId,
                        test.Organisation.Id,
                        recurring.Id,
                        true));

        Assert.Contains(
            "cannot be resumed",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Empty(
            await test.Db.RecurringSalesInvoiceGenerations
                .AsNoTracking()
                .Where(x =>
                    x.RecurringSalesInvoiceId == recurring.Id)
                .ToListAsync());
    }


    [Fact]
    public async Task UpdateAsync_ReplacesTemplateLinesAndPreservesGenerationHistory()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new RecurringSalesInvoiceService(
                test.Db,
                test.Access,
                test.SalesInvoices);

        var recurring =
            await service.CreateAsync(
                test.UserId,
                new RecurringSalesInvoiceRequest(
                    OrganisationId: test.Organisation.Id,
                    CustomerId: test.Customer.Id,
                    Frequency: RecurringSalesInvoiceFrequency.Monthly,
                    StartDate: new DateOnly(2027, 6, 1),
                    DueDays: 14,
                    Lines:
                    [
                        new RecurringSalesInvoiceLineRequest(
                            Description: "Original service",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id)
                    ]));

        var generated =
            await service.GenerateDueAsync(
                test.UserId,
                test.Organisation.Id,
                new DateOnly(2027, 6, 1));

        var originalInvoice =
            Assert.Single(generated);

        await service.UpdateAsync(
            test.UserId,
            recurring.Id,
            new RecurringSalesInvoiceRequest(
                OrganisationId: test.Organisation.Id,
                CustomerId: test.Customer.Id,
                Frequency: RecurringSalesInvoiceFrequency.Quarterly,
                StartDate: new DateOnly(2027, 7, 15),
                DueDays: 21,
                Lines:
                [
                    new RecurringSalesInvoiceLineRequest(
                        Description: "Updated service",
                        Quantity: 2m,
                        UnitPrice: 175m,
                        VatTreatment: VatTreatment.Standard,
                        RevenueAccountId: test.Account("4000").Id),
                    new RecurringSalesInvoiceLineRequest(
                        Description: "Additional service",
                        Quantity: 1m,
                        UnitPrice: 50m,
                        VatTreatment: VatTreatment.Standard,
                        RevenueAccountId: test.Account("4000").Id)
                ]));

        var reloaded =
            await test.Db.RecurringSalesInvoices
                .AsNoTracking()
                .Include(x => x.Lines)
                .SingleAsync(x => x.Id == recurring.Id);

        Assert.Equal(
            RecurringSalesInvoiceFrequency.Quarterly,
            reloaded.Frequency);

        Assert.Equal(
            new DateOnly(2027, 7, 15),
            reloaded.StartDate);

        Assert.Equal(
            new DateOnly(2027, 7, 15),
            reloaded.NextInvoiceDate);

        Assert.Equal(
            21,
            reloaded.DueDays);

        Assert.Equal(
            2,
            reloaded.Lines.Count);

        Assert.Contains(
            reloaded.Lines,
            x =>
                x.Description == "Updated service" &&
                x.Quantity == 2m &&
                x.UnitPrice == 175m);

        Assert.Contains(
            reloaded.Lines,
            x =>
                x.Description == "Additional service" &&
                x.Quantity == 1m &&
                x.UnitPrice == 50m);

        Assert.True(
            await test.Db.SalesInvoices
                .AsNoTracking()
                .AnyAsync(x => x.Id == originalInvoice.Id));

        var generation =
            await test.Db.RecurringSalesInvoiceGenerations
                .AsNoTracking()
                .SingleAsync(x =>
                    x.RecurringSalesInvoiceId == recurring.Id);

        Assert.Equal(
            originalInvoice.Id,
            generation.SalesInvoiceId);

        Assert.Equal(
            new DateOnly(2027, 6, 1),
            generation.ScheduledDate);
    }

}
