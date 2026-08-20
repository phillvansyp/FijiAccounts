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
}