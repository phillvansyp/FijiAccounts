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

        var report = await Reporting(test)
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

        var report = await Reporting(test)
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

        var report = await Reporting(test)
            .GetAsync(test.UserId,
                new BudgetReportRequest(test.Organisation.Id, 2026, 0m, 0m));

        Assert.Equal(0, report.AlertCount);
        Assert.False(Assert.Single(report.AccountsPerformance).RequiresAttention);
    }

    [Fact]
    public async Task GetAsync_DivisionScopeUsesOnlyMatchingBudgetAndActuals()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var structures = new EnterpriseStructureService(test.Db);
        var branch = await structures.AddBranchAsync(
            test.UserId, test.Organisation.Id, "NADI", "Nadi Branch");
        var retail = await structures.AddDivisionAsync(
            test.UserId, test.Organisation.Id, branch.Id, "RETAIL", "Retail");
        var services = await structures.AddDivisionAsync(
            test.UserId, test.Organisation.Id, branch.Id, "SERV", "Services");
        var month = new DateOnly(2026, 8, 1);
        var revenue = test.Account("4000");
        test.Db.AccountBudgets.AddRange(
            Budget(test, revenue, month, 2_000m),
            Budget(test, revenue, month, 800m, branch.Id, retail.Id),
            Budget(test, revenue, month, 1_200m, branch.Id, services.Id));
        test.Db.PostedJournals.Add(new PostedJournal
        {
            OrganisationId = test.Organisation.Id,
            SequenceNumber = 1,
            EntryDate = new DateOnly(2026, 8, 15),
            Reference = "DIMENSION-ACTUAL",
            PostedAt = DateTimeOffset.UtcNow,
            PostedByUserId = test.UserId,
            Lines =
            [
                new PostedJournalLine
                {
                    LedgerAccountId = revenue.Id,
                    BranchId = branch.Id,
                    DivisionId = retail.Id,
                    Description = "Retail revenue",
                    Credit = 700m
                },
                new PostedJournalLine
                {
                    LedgerAccountId = revenue.Id,
                    BranchId = branch.Id,
                    DivisionId = services.Id,
                    Description = "Services revenue",
                    Credit = 1_500m
                }
            ]
        });
        await test.Db.SaveChangesAsync();

        var report = await Reporting(test)
            .GetAsync(test.UserId, new BudgetReportRequest(
                test.Organisation.Id, 2026, BranchId: branch.Id, DivisionId: retail.Id));

        Assert.Equal(800m, report.RevenueBudget);
        Assert.Equal(700m, report.RevenueActual);
        Assert.Equal(branch.Id, report.BranchId);
        Assert.Equal(retail.Id, report.DivisionId);
        Assert.Contains("Retail", report.ScopeLabel);
    }

    [Fact]
    public async Task GetAsync_BranchScopeIncludesItsDivisionsAndExcludesOtherBranches()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var structures = new EnterpriseStructureService(test.Db);
        var nadi = await structures.AddBranchAsync(
            test.UserId, test.Organisation.Id, "NADI", "Nadi Branch");
        var retail = await structures.AddDivisionAsync(
            test.UserId, test.Organisation.Id, nadi.Id, "RETAIL", "Retail");
        var services = await structures.AddDivisionAsync(
            test.UserId, test.Organisation.Id, nadi.Id, "SERV", "Services");
        var suva = await structures.AddBranchAsync(
            test.UserId, test.Organisation.Id, "SUVA", "Suva Branch");
        var suvaDivision = await structures.AddDivisionAsync(
            test.UserId, test.Organisation.Id, suva.Id, "RETAIL", "Retail");
        var month = new DateOnly(2026, 8, 1);
        var revenue = test.Account("4000");
        test.Db.AccountBudgets.AddRange(
            Budget(test, revenue, month, 2_000m, nadi.Id),
            Budget(test, revenue, month, 900m, suva.Id));
        test.Db.PostedJournals.Add(new PostedJournal
        {
            OrganisationId = test.Organisation.Id,
            SequenceNumber = 1,
            EntryDate = new DateOnly(2026, 8, 15),
            Reference = "BRANCH-ACTUAL",
            PostedAt = DateTimeOffset.UtcNow,
            PostedByUserId = test.UserId,
            Lines =
            [
                Line(revenue.Id, nadi.Id, retail.Id, "Nadi retail", 700m),
                Line(revenue.Id, nadi.Id, services.Id, "Nadi services", 1_100m),
                Line(revenue.Id, suva.Id, suvaDivision.Id, "Suva retail", 850m)
            ]
        });
        await test.Db.SaveChangesAsync();

        var report = await Reporting(test).GetAsync(
            test.UserId,
            new BudgetReportRequest(test.Organisation.Id, 2026, BranchId: nadi.Id));

        Assert.Equal(2_000m, report.RevenueBudget);
        Assert.Equal(1_800m, report.RevenueActual);
        Assert.Equal(nadi.Id, report.BranchId);
        Assert.Null(report.DivisionId);
        Assert.Contains("Nadi", report.ScopeLabel);
    }

    [Fact]
    public async Task GetAsync_RestrictedMemberCannotViewOrganisationScope()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var branch = await test.Db.Branches.Include(x => x.Divisions)
            .SingleAsync(x => x.OrganisationId == test.Organisation.Id && x.IsDefault);
        var division = branch.Divisions.Single(x => x.IsDefault);
        var member = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "budget-restricted@example.com",
            NormalizedUserName = "BUDGET-RESTRICTED@EXAMPLE.COM",
            Email = "budget-restricted@example.com",
            NormalizedEmail = "BUDGET-RESTRICTED@EXAMPLE.COM",
            EmailConfirmed = true
        };
        test.Db.Users.Add(member);
        test.Db.OrganisationMemberships.Add(new OrganisationMembership
        {
            OrganisationId = test.Organisation.Id,
            UserId = member.Id,
            User = member,
            Role = OrganisationRole.Bookkeeper
        });
        await test.Db.SaveChangesAsync();
        await test.Access.SetDimensionAccessModeAsync(
            test.UserId, test.Organisation.Id, member.Id, DimensionAccessMode.Restricted);
        await test.Access.AddDimensionAccessGrantAsync(
            test.UserId, test.Organisation.Id, member.Id, branch.Id, division.Id);

        var service = Reporting(test);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(
            member.Id, new BudgetReportRequest(test.Organisation.Id, 2026)));

        var permitted = await service.GetAsync(member.Id, new BudgetReportRequest(
            test.Organisation.Id, 2026, BranchId: branch.Id, DivisionId: division.Id));
        Assert.Equal(division.Id, permitted.DivisionId);
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
            Reporting(test).GetAsync(
                test.UserId, new BudgetReportRequest(other.Id, 2026)));
    }

    private static AccountBudget Budget(
        AccountingTestDatabase test,
        LedgerAccount account,
        DateOnly month,
        decimal amount,
        Guid? branchId = null,
        Guid? divisionId = null) => new()
        {
            OrganisationId = test.Organisation.Id,
            LedgerAccountId = account.Id,
            ScopeKey = divisionId is Guid selectedDivisionId
                ? $"division:{selectedDivisionId:N}"
                : branchId is Guid selectedBranchId
                    ? $"branch:{selectedBranchId:N}"
                    : "organisation",
            BranchId = branchId,
            DivisionId = divisionId,
            Month = month,
            Amount = amount,
            UpdatedByUserId = test.UserId
        };

    private static BudgetReportingService Reporting(AccountingTestDatabase test) =>
        new(test.Db, new BudgetScopeService(test.Db, test.Access));

    private static PostedJournalLine Line(
        Guid accountId,
        Guid branchId,
        Guid divisionId,
        string description,
        decimal credit) => new()
        {
            LedgerAccountId = accountId,
            BranchId = branchId,
            DivisionId = divisionId,
            Description = description,
            Credit = credit
        };
}
