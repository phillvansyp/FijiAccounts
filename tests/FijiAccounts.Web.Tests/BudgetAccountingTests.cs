using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class BudgetAccountingTests
{
    [Fact]
    public async Task SetAsync_CreatesBudgetAndNormalizesMonth()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new BudgetService(
                test.Db,
                test.Access);

        var budget =
            await service.SetAsync(
                test.UserId,
                new BudgetRequest(
                    OrganisationId: test.Organisation.Id,
                    AccountId: test.Account("6500").Id,
                    Month: new DateOnly(2026, 8, 20),
                    Amount: 1_500m));

        Assert.Equal(
            new DateOnly(2026, 8, 1),
            budget.Month);

        Assert.Equal(
            1_500m,
            budget.Amount);

        Assert.Equal(
            test.Account("6500").Id,
            budget.LedgerAccountId);

        var stored =
            await test.Db.AccountBudgets
                .AsNoTracking()
                .SingleAsync(x => x.Id == budget.Id);

        Assert.Equal(
            new DateOnly(2026, 8, 1),
            stored.Month);
    }

    [Fact]
    public async Task SetAsync_ExistingMonthUpdatesInsteadOfCreatingDuplicate()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new BudgetService(
                test.Db,
                test.Access);

        var first =
            await service.SetAsync(
                test.UserId,
                new BudgetRequest(
                    OrganisationId: test.Organisation.Id,
                    AccountId: test.Account("6500").Id,
                    Month: new DateOnly(2026, 8, 5),
                    Amount: 1_000m));

        var second =
            await service.SetAsync(
                test.UserId,
                new BudgetRequest(
                    OrganisationId: test.Organisation.Id,
                    AccountId: test.Account("6500").Id,
                    Month: new DateOnly(2026, 8, 25),
                    Amount: 2_000m));

        Assert.Equal(
            first.Id,
            second.Id);

        Assert.Equal(
            2_000m,
            second.Amount);

        Assert.Equal(
            1,
            await test.Db.AccountBudgets.CountAsync(
                x =>
                    x.OrganisationId == test.Organisation.Id &&
                    x.LedgerAccountId == test.Account("6500").Id &&
                    x.Month == new DateOnly(2026, 8, 1)));
    }

    [Fact]
    public async Task SetAsync_WhenAmountIsNegative_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new BudgetService(
                test.Db,
                test.Access);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.SetAsync(
                        test.UserId,
                        new BudgetRequest(
                            OrganisationId: test.Organisation.Id,
                            AccountId: test.Account("6500").Id,
                            Month: new DateOnly(2026, 8, 1),
                            Amount: -1m)));

        Assert.Contains(
            "negative",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Empty(
            await test.Db.AccountBudgets
                .ToListAsync());
    }

    [Fact]
    public async Task SetAsync_WhenAccountIsInactive_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var expense =
            test.Account("6500");

        expense.IsActive = false;
        await test.Db.SaveChangesAsync();

        var service =
            new BudgetService(
                test.Db,
                test.Access);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.SetAsync(
                        test.UserId,
                        new BudgetRequest(
                            OrganisationId: test.Organisation.Id,
                            AccountId: expense.Id,
                            Month: new DateOnly(2026, 8, 1),
                            Amount: 1_000m)));

        Assert.Contains(
            "active revenue or expense account",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SetAsync_WhenAccountHasInvalidType_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank =
            test.Account("1000");

        var service =
            new BudgetService(
                test.Db,
                test.Access);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.SetAsync(
                        test.UserId,
                        new BudgetRequest(
                            OrganisationId: test.Organisation.Id,
                            AccountId: bank.Id,
                            Month: new DateOnly(2026, 8, 1),
                            Amount: 1_000m)));

        Assert.Contains(
            "revenue or expense",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SetAsync_AllowsRevenueAccount()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new BudgetService(
                test.Db,
                test.Access);

        var budget =
            await service.SetAsync(
                test.UserId,
                new BudgetRequest(
                    OrganisationId: test.Organisation.Id,
                    AccountId: test.Account("4000").Id,
                    Month: new DateOnly(2026, 8, 1),
                    Amount: 5_000m));

        Assert.Equal(
            test.Account("4000").Id,
            budget.LedgerAccountId);

        Assert.Equal(
            5_000m,
            budget.Amount);
    }

    [Fact]
    public async Task SetAsync_CreatesAndUpdatesAuditEvents()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new BudgetService(
                test.Db,
                test.Access);

        await service.SetAsync(
            test.UserId,
            new BudgetRequest(
                OrganisationId: test.Organisation.Id,
                AccountId: test.Account("6500").Id,
                Month: new DateOnly(2026, 8, 1),
                Amount: 1_000m));

        await service.SetAsync(
            test.UserId,
            new BudgetRequest(
                OrganisationId: test.Organisation.Id,
                AccountId: test.Account("6500").Id,
                Month: new DateOnly(2026, 8, 20),
                Amount: 2_000m));

        var events =
    await test.Db.AuditEvents
        .AsNoTracking()
        .Where(x =>
            x.OrganisationId == test.Organisation.Id &&
            x.EntityType == "AccountBudget")
        .ToListAsync();

        Assert.Equal(
            2,
            events.Count);

        Assert.Contains(
    events,
    x => x.EventType == "BudgetCreated");

Assert.Contains(
    events,
    x => x.EventType == "BudgetUpdated");
    }
}