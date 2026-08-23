using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class RecurringSupplierBillServiceTests
{
    [Fact]
    public async Task CreateAsync_CreatesActiveTemplateWithLines()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new RecurringSupplierBillService(
                test.Db,
                test.Access,
                test.Purchasing);

        var recurring =
            await service.CreateAsync(
                test.UserId,
                new RecurringSupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "RENT",
                    Frequency: RecurringSupplierBillFrequency.Monthly,
                    StartDate: new DateOnly(2026, 9, 1),
                    DueDays: 14,
                    Lines:
                    [
                        new RecurringSupplierBillLineRequest(
                            Description: "Monthly rent",
                            Quantity: 1m,
                            UnitPrice: 1_000m,
                            VatTreatment: VatTreatment.Standard,
                            ExpenseAccountId: test.Account("6500").Id)
                    ]));

        var reloaded =
            await test.Db.RecurringSupplierBills
                .AsNoTracking()
                .Include(x => x.Lines)
                .SingleAsync(x => x.Id == recurring.Id);

        Assert.True(reloaded.IsActive);
        Assert.Equal(
            new DateOnly(2026, 9, 1),
            reloaded.StartDate);
        Assert.Equal(
            new DateOnly(2026, 9, 1),
            reloaded.NextBillDate);
        Assert.Equal(14, reloaded.DueDays);
        Assert.Single(reloaded.Lines);
        Assert.Equal(
            "Monthly rent",
            reloaded.Lines[0].Description);
    }

    [Fact]
    public async Task GenerateDueAsync_CreatesSupplierBillAndAdvancesSchedule()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new RecurringSupplierBillService(
                test.Db,
                test.Access,
                test.Purchasing);

        var recurring =
            await service.CreateAsync(
                test.UserId,
                new RecurringSupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "RENT",
                    Frequency: RecurringSupplierBillFrequency.Monthly,
                    StartDate: new DateOnly(2026, 9, 1),
                    DueDays: 14,
                    Lines:
                    [
                        new RecurringSupplierBillLineRequest(
                            Description: "Monthly rent",
                            Quantity: 1m,
                            UnitPrice: 1_000m,
                            VatTreatment: VatTreatment.Standard,
                            ExpenseAccountId: test.Account("6500").Id)
                    ]));

        var generated =
            await service.GenerateDueAsync(
                test.UserId,
                test.Organisation.Id,
                new DateOnly(2026, 9, 1));

        var bill =
            Assert.Single(generated);

        Assert.Equal(
            new DateOnly(2026, 9, 1),
            bill.BillDate);
        Assert.Equal(
            new DateOnly(2026, 9, 15),
            bill.DueDate);
        Assert.Equal(
            "RENT-20260901",
            bill.SupplierReference);

        var generation =
            await test.Db.RecurringSupplierBillGenerations
                .AsNoTracking()
                .SingleAsync(x =>
                    x.RecurringSupplierBillId == recurring.Id);

        Assert.Equal(
            new DateOnly(2026, 9, 1),
            generation.ScheduledDate);
        Assert.Equal(
            bill.Id,
            generation.SupplierBillId);

        var reloadedTemplate =
            await test.Db.RecurringSupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == recurring.Id);

        Assert.Equal(
            new DateOnly(2026, 10, 1),
            reloadedTemplate.NextBillDate);
    }

    [Fact]
    public async Task GenerateDueAsync_CalledTwice_DoesNotDuplicateOccurrence()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new RecurringSupplierBillService(
                test.Db,
                test.Access,
                test.Purchasing);

        var recurring =
            await service.CreateAsync(
                test.UserId,
                new RecurringSupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "SOFTWARE",
                    Frequency: RecurringSupplierBillFrequency.Monthly,
                    StartDate: new DateOnly(2026, 9, 1),
                    DueDays: 7,
                    Lines:
                    [
                        new RecurringSupplierBillLineRequest(
                            Description: "Software subscription",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            ExpenseAccountId: test.Account("6500").Id)
                    ]));

        var first =
            await service.GenerateDueAsync(
                test.UserId,
                test.Organisation.Id,
                new DateOnly(2026, 9, 1));

        await test.Db.RecurringSupplierBills
            .Where(x => x.Id == recurring.Id)
            .ExecuteUpdateAsync(update =>
                update.SetProperty(
                    x => x.NextBillDate,
                    new DateOnly(2026, 9, 1)));
        test.Db.ChangeTracker.Clear();

        var second =
            await service.GenerateDueAsync(
                test.UserId,
                test.Organisation.Id,
                new DateOnly(2026, 9, 1));

        Assert.Single(first);
        Assert.Empty(second);

        Assert.Equal(
            1,
            await test.Db.RecurringSupplierBillGenerations.CountAsync(
                x =>
                    x.RecurringSupplierBillId == recurring.Id &&
                    x.ScheduledDate ==
                        new DateOnly(2026, 9, 1)));

        Assert.Equal(
            1,
            await test.Db.SupplierBills.CountAsync(
                x =>
                    x.OrganisationId == test.Organisation.Id &&
                    x.SupplierReference ==
                        "SOFTWARE-20260901"));
        Assert.Equal(
            new DateOnly(2026, 10, 1),
            await test.Db.RecurringSupplierBills
                .Where(x => x.Id == recurring.Id)
                .Select(x => x.NextBillDate)
                .SingleAsync());
    }

    [Fact]
    public async Task GenerateDueAsync_ThroughLaterDate_CatchesUpOccurrences()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new RecurringSupplierBillService(
                test.Db,
                test.Access,
                test.Purchasing);

        var recurring =
            await service.CreateAsync(
                test.UserId,
                new RecurringSupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "RENT",
                    Frequency: RecurringSupplierBillFrequency.Monthly,
                    StartDate: new DateOnly(2026, 7, 1),
                    DueDays: 14,
                    Lines:
                    [
                        new RecurringSupplierBillLineRequest(
                            Description: "Monthly rent",
                            Quantity: 1m,
                            UnitPrice: 1_000m,
                            VatTreatment: VatTreatment.Standard,
                            ExpenseAccountId: test.Account("6500").Id)
                    ]));

        var generated =
            await service.GenerateDueAsync(
                test.UserId,
                test.Organisation.Id,
                new DateOnly(2026, 9, 1));

        Assert.Equal(3, generated.Count);

        var dates =
            await test.Db.RecurringSupplierBillGenerations
                .AsNoTracking()
                .Where(x =>
                    x.RecurringSupplierBillId ==
                        recurring.Id)
                .OrderBy(x => x.ScheduledDate)
                .Select(x => x.ScheduledDate)
                .ToListAsync();

        Assert.Equal(
            [
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 9, 1)
            ],
            dates);

        var reloadedTemplate =
            await test.Db.RecurringSupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == recurring.Id);

        Assert.Equal(
            new DateOnly(2026, 10, 1),
            reloadedTemplate.NextBillDate);
    }

    [Fact]
    public async Task GenerateDueAsync_WhenOccurrenceIsInsideLockedAccountingPeriod_IsRejectedWithoutGeneration()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new RecurringSupplierBillService(
                test.Db,
                test.Access,
                test.Purchasing);

        var recurring =
            await service.CreateAsync(
                test.UserId,
                new RecurringSupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "RENT",
                    Frequency: RecurringSupplierBillFrequency.Monthly,
                    StartDate: new DateOnly(2026, 9, 1),
                    DueDays: 14,
                    Lines:
                    [
                        new RecurringSupplierBillLineRequest(
                            Description: "Monthly rent",
                            Quantity: 1m,
                            UnitPrice: 1_000m,
                            VatTreatment: VatTreatment.Standard,
                            ExpenseAccountId: test.Account("6500").Id)
                    ]));

        test.Db.AccountingPeriods.Add(
            new AccountingPeriod
            {
                OrganisationId = test.Organisation.Id,
                Name = "September 2026",
                StartsOn = new DateOnly(2026, 9, 1),
                EndsOn = new DateOnly(2026, 9, 30),
                IsLocked = true
            });

        await test.Db.SaveChangesAsync();

        var supplierBillCountBefore =
            await test.Db.SupplierBills.CountAsync();

        var generationCountBefore =
            await test.Db.RecurringSupplierBillGenerations.CountAsync();

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.GenerateDueAsync(
                        test.UserId,
                        test.Organisation.Id,
                        new DateOnly(2026, 9, 1)));

        Assert.Contains(
            "locked",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            supplierBillCountBefore,
            await test.Db.SupplierBills.CountAsync());

        Assert.Equal(
            generationCountBefore,
            await test.Db.RecurringSupplierBillGenerations.CountAsync());

        var reloadedTemplate =
            await test.Db.RecurringSupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == recurring.Id);

        Assert.Equal(
            new DateOnly(2026, 9, 1),
            reloadedTemplate.NextBillDate);
    }

    [Fact]
    public async Task CreateAsync_WhenLineIsInvalid_IsRejectedWithoutTemplate()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new RecurringSupplierBillService(
                test.Db,
                test.Access,
                test.Purchasing);

        var countBefore =
            await test.Db.RecurringSupplierBills.CountAsync();

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new RecurringSupplierBillRequest(
                            OrganisationId: test.Organisation.Id,
                            SupplierId: test.Supplier.Id,
                            SupplierReference: "INVALID-LINE",
                            Frequency: RecurringSupplierBillFrequency.Monthly,
                            StartDate: new DateOnly(2026, 9, 1),
                            DueDays: 14,
                            Lines:
                            [
                                new RecurringSupplierBillLineRequest(
                                    Description: "",
                                    Quantity: 0m,
                                    UnitPrice: -1m,
                                    VatTreatment: VatTreatment.Standard,
                                    ExpenseAccountId: test.Account("6500").Id)
                            ])));

        Assert.Contains(
            "description",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            countBefore,
            await test.Db.RecurringSupplierBills.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_WhenExpenseAccountIsInvalid_IsRejectedWithoutTemplate()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new RecurringSupplierBillService(
                test.Db,
                test.Access,
                test.Purchasing);

        var countBefore =
            await test.Db.RecurringSupplierBills.CountAsync();

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new RecurringSupplierBillRequest(
                            OrganisationId: test.Organisation.Id,
                            SupplierId: test.Supplier.Id,
                            SupplierReference: "INVALID-ACCOUNT",
                            Frequency: RecurringSupplierBillFrequency.Monthly,
                            StartDate: new DateOnly(2026, 9, 1),
                            DueDays: 14,
                            Lines:
                            [
                                new RecurringSupplierBillLineRequest(
                                    Description: "Invalid account",
                                    Quantity: 1m,
                                    UnitPrice: 100m,
                                    VatTreatment: VatTreatment.Standard,
                                    ExpenseAccountId: test.Account("4000").Id)
                            ])));

        Assert.Contains(
            "expense or asset account",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            countBefore,
            await test.Db.RecurringSupplierBills.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_WhenProductItemIsUnavailable_IsRejectedWithoutTemplate()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new RecurringSupplierBillService(
                test.Db,
                test.Access,
                test.Purchasing);

        var countBefore =
            await test.Db.RecurringSupplierBills.CountAsync();

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new RecurringSupplierBillRequest(
                            OrganisationId: test.Organisation.Id,
                            SupplierId: test.Supplier.Id,
                            SupplierReference: "INVALID-PRODUCT",
                            Frequency: RecurringSupplierBillFrequency.Monthly,
                            StartDate: new DateOnly(2026, 9, 1),
                            DueDays: 14,
                            Lines:
                            [
                                new RecurringSupplierBillLineRequest(
                                    Description: "Unavailable product",
                                    Quantity: 1m,
                                    UnitPrice: 100m,
                                    VatTreatment: VatTreatment.Standard,
                                    ExpenseAccountId: test.Account("6500").Id,
                                    ProductItemId: Guid.NewGuid())
                            ])));

        Assert.Contains(
            "unavailable",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            countBefore,
            await test.Db.RecurringSupplierBills.CountAsync());
    }

    [Fact]
    public async Task UpdateAsync_ReplacesTemplateLinesAndPreservesGenerationHistory()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new RecurringSupplierBillService(
                test.Db,
                test.Access,
                test.Purchasing);

        var recurring =
            await service.CreateAsync(
                test.UserId,
                new RecurringSupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "RENT",
                    Frequency: RecurringSupplierBillFrequency.Monthly,
                    StartDate: new DateOnly(2026, 9, 1),
                    DueDays: 14,
                    Lines:
                    [
                        new RecurringSupplierBillLineRequest(
                            Description: "Monthly rent",
                            Quantity: 1m,
                            UnitPrice: 1_000m,
                            VatTreatment: VatTreatment.Standard,
                            ExpenseAccountId: test.Account("6500").Id)
                    ]));

        var generated =
            await service.GenerateDueAsync(
                test.UserId,
                test.Organisation.Id,
                new DateOnly(2026, 9, 1));

        Assert.Single(generated);

        var generation =
            await test.Db.RecurringSupplierBillGenerations
                .AsNoTracking()
                .SingleAsync(x =>
                    x.RecurringSupplierBillId == recurring.Id);

        var originalSupplierBillId =
            generation.SupplierBillId;

        await service.UpdateAsync(
            test.UserId,
            recurring.Id,
            new RecurringSupplierBillRequest(
                OrganisationId: test.Organisation.Id,
                SupplierId: test.Supplier.Id,
                SupplierReference: "UPDATED-RENT",
                Frequency: RecurringSupplierBillFrequency.Quarterly,
                StartDate: new DateOnly(2026, 10, 1),
                DueDays: 30,
                Lines:
                [
                    new RecurringSupplierBillLineRequest(
                        Description: "Updated rent",
                        Quantity: 2m,
                        UnitPrice: 750m,
                        VatTreatment: VatTreatment.Standard,
                        ExpenseAccountId: test.Account("6500").Id)
                ]));

        var reloaded =
            await test.Db.RecurringSupplierBills
                .AsNoTracking()
                .Include(x => x.Lines)
                .SingleAsync(x => x.Id == recurring.Id);

        Assert.Equal(
            "UPDATED-RENT",
            reloaded.SupplierReference);

        Assert.Equal(
            RecurringSupplierBillFrequency.Quarterly,
            reloaded.Frequency);

        Assert.Equal(
            new DateOnly(2026, 10, 1),
            reloaded.StartDate);

        Assert.Equal(
            new DateOnly(2026, 10, 1),
            reloaded.NextBillDate);

        Assert.Equal(
            30,
            reloaded.DueDays);

        var line =
            Assert.Single(reloaded.Lines);

        Assert.Equal(
            "Updated rent",
            line.Description);

        Assert.Equal(
            2m,
            line.Quantity);

        Assert.Equal(
            750m,
            line.UnitPrice);

        var reloadedGeneration =
            await test.Db.RecurringSupplierBillGenerations
                .AsNoTracking()
                .SingleAsync(x =>
                    x.RecurringSupplierBillId == recurring.Id);

        Assert.Equal(
            originalSupplierBillId,
            reloadedGeneration.SupplierBillId);

        Assert.True(
            await test.Db.SupplierBills
                .AsNoTracking()
                .AnyAsync(x =>
                    x.Id == originalSupplierBillId));
    }

    [Fact]
    public async Task SetActiveAsync_WhenPaused_PreventsGeneration()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new RecurringSupplierBillService(
                test.Db,
                test.Access,
                test.Purchasing);

        var recurring =
            await service.CreateAsync(
                test.UserId,
                new RecurringSupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "PAUSED",
                    Frequency: RecurringSupplierBillFrequency.Monthly,
                    StartDate: new DateOnly(2026, 9, 1),
                    DueDays: 14,
                    Lines:
                    [
                        new RecurringSupplierBillLineRequest(
                            Description: "Paused bill",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            ExpenseAccountId: test.Account("6500").Id)
                    ]));

        await service.SetActiveAsync(
            test.UserId,
            test.Organisation.Id,
            recurring.Id,
            false);

        var generated =
            await service.GenerateDueAsync(
                test.UserId,
                test.Organisation.Id,
                new DateOnly(2026, 12, 1));

        Assert.Empty(generated);

        var reloaded =
            await test.Db.RecurringSupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == recurring.Id);

        Assert.False(reloaded.IsActive);

        Assert.Equal(
            new DateOnly(2026, 9, 1),
            reloaded.NextBillDate);

        Assert.False(
            await test.Db.RecurringSupplierBillGenerations
                .AsNoTracking()
                .AnyAsync(x =>
                    x.RecurringSupplierBillId == recurring.Id));
    }

    [Fact]
    public async Task SetActiveAsync_WhenResumedFromLaterDate_SkipsMissedOccurrences()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new RecurringSupplierBillService(
                test.Db,
                test.Access,
                test.Purchasing);

        var recurring =
            await service.CreateAsync(
                test.UserId,
                new RecurringSupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "RESUME",
                    Frequency: RecurringSupplierBillFrequency.Monthly,
                    StartDate: new DateOnly(2026, 9, 1),
                    DueDays: 14,
                    Lines:
                    [
                        new RecurringSupplierBillLineRequest(
                            Description: "Resume bill",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            ExpenseAccountId: test.Account("6500").Id)
                    ]));

        await service.SetActiveAsync(
            test.UserId,
            test.Organisation.Id,
            recurring.Id,
            false);

        await service.SetActiveAsync(
            test.UserId,
            test.Organisation.Id,
            recurring.Id,
            true,
            new DateOnly(2026, 12, 1));

        var generated =
            await service.GenerateDueAsync(
                test.UserId,
                test.Organisation.Id,
                new DateOnly(2026, 12, 1));

        var bill =
            Assert.Single(generated);

        Assert.Equal(
            new DateOnly(2026, 12, 1),
            bill.BillDate);

        var reloaded =
            await test.Db.RecurringSupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == recurring.Id);

        Assert.True(reloaded.IsActive);

        Assert.Equal(
            new DateOnly(2027, 1, 1),
            reloaded.NextBillDate);

        var generations =
            await test.Db.RecurringSupplierBillGenerations
                .AsNoTracking()
                .Where(x =>
                    x.RecurringSupplierBillId == recurring.Id)
                .OrderBy(x => x.ScheduledDate)
                .ToListAsync();

        Assert.Single(generations);

        Assert.Equal(
            new DateOnly(2026, 12, 1),
            generations[0].ScheduledDate);
    }

    [Fact]
    public async Task EndAsync_DeactivatesTemplateAndPreservesGenerationHistory()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new RecurringSupplierBillService(
                test.Db,
                test.Access,
                test.Purchasing);

        var recurring =
            await service.CreateAsync(
                test.UserId,
                new RecurringSupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "ENDED",
                    Frequency: RecurringSupplierBillFrequency.Monthly,
                    StartDate: new DateOnly(2026, 9, 1),
                    DueDays: 14,
                    Lines:
                    [
                        new RecurringSupplierBillLineRequest(
                            Description: "Ended recurring bill",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            ExpenseAccountId: test.Account("6500").Id)
                    ]));

        var generated =
            await service.GenerateDueAsync(
                test.UserId,
                test.Organisation.Id,
                new DateOnly(2026, 9, 1));

        var originalBill =
            Assert.Single(generated);

        await service.EndAsync(
            test.UserId,
            test.Organisation.Id,
            recurring.Id);

        var reloaded =
            await test.Db.RecurringSupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == recurring.Id);

        Assert.False(reloaded.IsActive);

        Assert.True(
            await test.Db.SupplierBills
                .AsNoTracking()
                .AnyAsync(x => x.Id == originalBill.Id));

        var generation =
            await test.Db.RecurringSupplierBillGenerations
                .AsNoTracking()
                .SingleAsync(x =>
                    x.RecurringSupplierBillId == recurring.Id);

        Assert.Equal(
            originalBill.Id,
            generation.SupplierBillId);

        var future =
            await service.GenerateDueAsync(
                test.UserId,
                test.Organisation.Id,
                new DateOnly(2026, 12, 1));

        Assert.Empty(future);

        Assert.Single(
            await test.Db.RecurringSupplierBillGenerations
                .AsNoTracking()
                .Where(x =>
                    x.RecurringSupplierBillId == recurring.Id)
                .ToListAsync());
    }

    [Fact]
    public async Task SetActiveAsync_WhenEnded_CannotBeResumed()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new RecurringSupplierBillService(
                test.Db,
                test.Access,
                test.Purchasing);

        var recurring =
            await service.CreateAsync(
                test.UserId,
                new RecurringSupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "ENDED-NO-RESUME",
                    Frequency: RecurringSupplierBillFrequency.Monthly,
                    StartDate: new DateOnly(2026, 9, 1),
                    DueDays: 14,
                    Lines:
                    [
                        new RecurringSupplierBillLineRequest(
                            Description: "Ended recurring bill",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            ExpenseAccountId: test.Account("6500").Id)
                    ]));

        await service.EndAsync(
            test.UserId,
            test.Organisation.Id,
            recurring.Id);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.SetActiveAsync(
                        test.UserId,
                        test.Organisation.Id,
                        recurring.Id,
                        true,
                        new DateOnly(2026, 10, 1)));

        Assert.Contains(
            "cannot be resumed",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        var reloaded =
            await test.Db.RecurringSupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == recurring.Id);

        Assert.False(reloaded.IsActive);

        Assert.Equal(
            RecurringSupplierBillStatus.Ended,
            reloaded.Status);
    }

    [Fact]
    public async Task GenerateDueAsync_MonthlyOnThirtyFirst_ReturnsToThirtyFirstAfterShortMonth()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new RecurringSupplierBillService(
                test.Db,
                test.Access,
                test.Purchasing);

        var recurring =
            await service.CreateAsync(
                test.UserId,
                new RecurringSupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "MONTH-END",
                    Frequency: RecurringSupplierBillFrequency.Monthly,
                    StartDate: new DateOnly(2027, 1, 31),
                    DueDays: 14,
                    Lines:
                    [
                        new RecurringSupplierBillLineRequest(
                            Description: "Month end bill",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            ExpenseAccountId: test.Account("6500").Id)
                    ]));

        var generated =
            await service.GenerateDueAsync(
                test.UserId,
                test.Organisation.Id,
                new DateOnly(2027, 3, 31));

        Assert.Equal(
            3,
            generated.Count);

        var generations =
            await test.Db.RecurringSupplierBillGenerations
                .AsNoTracking()
                .Where(x =>
                    x.RecurringSupplierBillId == recurring.Id)
                .OrderBy(x => x.ScheduledDate)
                .ToListAsync();

        Assert.Equal(
            [
                new DateOnly(2027, 1, 31),
                new DateOnly(2027, 2, 28),
                new DateOnly(2027, 3, 31)
            ],
            generations
                .Select(x => x.ScheduledDate)
                .ToArray());

        var reloaded =
            await test.Db.RecurringSupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == recurring.Id);

        Assert.Equal(
            new DateOnly(2027, 4, 30),
            reloaded.NextBillDate);
    }

}
