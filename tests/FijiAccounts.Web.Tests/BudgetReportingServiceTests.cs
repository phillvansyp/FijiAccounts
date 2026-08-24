using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class BudgetReportingServiceTests
{
    [Fact]
    public async Task GetAsync_CalculatesAdverseVariancesAndThresholdAlerts()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var month = new DateOnly(2026, 8, 1);
        test.Db.AccountBudgets.AddRange(
            Budget(test, test.Account("4000"), month, 1_000m),
            Budget(test, test.Account("6500"), month, 500m));
        test.Db.PostedJournals.Add(new PostedJournal
        {
            OrganisationId = test.Organisation.Id,
            SequenceNumber = 1,
            EntryDate = new DateOnly(2026, 8, 15),
            Reference = "BUDGET-ACTUAL",
            PostedAt = DateTimeOffset.UtcNow,
            PostedByUserId = test.UserId,
            Lines =
            [
                new PostedJournalLine
                {
                    LedgerAccountId = test.Account("4000").Id,
                    Description = "Revenue actual",
                    Credit = 800m
                },
                new PostedJournalLine
                {
                    LedgerAccountId = test.Account("6500").Id,
                    Description = "Expense actual",
                    Debit = 700m
                }
            ]
        });
        await test.Db.SaveChangesAsync();

        var report = await new BudgetReportingService(test.Db, test.Access)
            .GetAsync(test.UserId,
                new BudgetReportRequest(test.Organisation.Id, 2026, 10m, 100m));

        Assert.Equal(2, report.AlertCount);
        var revenue = Assert.Single(report.AccountsPerformance,
            x => x.AccountId == test.Account("4000").Id);
        Assert.Equal(-200m, revenue.FavourableVariance);
        Assert.Equal(200m, revenue.AdverseVariance);
        Assert.Equal(20m, revenue.AdverseVariancePercent);
        Assert.True(revenue.RequiresAttention);
        var expense = Assert.Single(report.AccountsPerformance,
            x => x.AccountId == test.Account("6500").Id);
        Assert.Equal(-200m, expense.FavourableVariance);
        Assert.Equal(40m, expense.AdverseVariancePercent);
        Assert.True(report.Months[7].RequiresAttention);
        Assert.Equal(-400m, report.Months[7].FavourableVariance);
    }

    [Fact]
    public async Task GetAsync_AlertRequiresAmountAndPercentageThresholds()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Db.AccountBudgets.Add(Budget(
            test, test.Account("6500"), new DateOnly(2026, 8, 1), 500m));
        test.Db.PostedJournals.Add(new PostedJournal
        {
            OrganisationId = test.Organisation.Id,
            SequenceNumber = 1,
            EntryDate = new DateOnly(2026, 8, 15),
            Reference = "BELOW-AMOUNT",
            PostedAt = DateTimeOffset.UtcNow,
            PostedByUserId = test.UserId,
            Lines =
            [
                new PostedJournalLine
                {
                    LedgerAccountId = test.Account("6500").Id,
                    Description = "Expense actual",
                    Debit = 700m
                }
            ]
        });
        await test.Db.SaveChangesAsync();

        var report = await new BudgetReportingService(test.Db, test.Access)
            .GetAsync(test.UserId,
                new BudgetReportRequest(test.Organisation.Id, 2026, 10m, 300m));

        Assert.Equal(0, report.AlertCount);
        Assert.False(Assert.Single(report.AccountsPerformance).RequiresAttention);
    }

    [Fact]
    public async Task GetAsync_ZeroThresholdsDoNotAlertForOnTargetAccount()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Db.AccountBudgets.Add(Budget(
            test, test.Account("6500"), new DateOnly(2026, 8, 1), 500m));
        test.Db.PostedJournals.Add(new PostedJournal
        {
            OrganisationId = test.Organisation.Id,
            SequenceNumber = 1,
            EntryDate = new DateOnly(2026, 8, 15),
            Reference = "ON-TARGET",
            PostedAt = DateTimeOffset.UtcNow,
            PostedByUserId = test.UserId,
            Lines =
            [
                new PostedJournalLine
                {
                    LedgerAccountId = test.Account("6500").Id,
                    Description = "Expense actual",
                    Debit = 500m
                }
            ]
        });
        await test.Db.SaveChangesAsync();

        var report = await new BudgetReportingService(test.Db, test.Access)
            .GetAsync(test.UserId,
                new BudgetReportRequest(test.Organisation.Id, 2026, 0m, 0m));

        Assert.Equal(0, report.AlertCount);
        Assert.False(Assert.Single(report.AccountsPerformance).RequiresAttention);
    }

    [Fact]
    public async Task GetAsync_RejectsCrossTenantAccess()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var other = new Organisation
        {
            LegalName = "Other Budget Company",
            CountryCode = "FJ",
            BaseCurrency = "FJD",
            Kind = OrganisationKind.Business
        };
        test.Db.Organisations.Add(other);
        await test.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new BudgetReportingService(test.Db, test.Access).GetAsync(
                test.UserId, new BudgetReportRequest(other.Id, 2026)));
    }

    private static AccountBudget Budget(
        AccountingTestDatabase test,
        LedgerAccount account,
        DateOnly month,
        decimal amount) => new()
        {
            OrganisationId = test.Organisation.Id,
            LedgerAccountId = account.Id,
            Month = month,
            Amount = amount,
            UpdatedByUserId = test.UserId
        };
}
