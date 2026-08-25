using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class ProjectWipPostingServiceTests
{
    [Fact]
    public async Task PostAsync_PostsContractAssetTrueUpAndPreventsDuplicateDate()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var (branch, division) = await DefaultDimensionAsync(test);
        var project = await CreateActiveProjectAsync(test, division.Id, "JOB-WIP-ASSET");
        await ConfigureAccountsAsync(test);
        await PostCostAndRevenueAsync(
            test, branch, division, project, 40_000m, 30_000m, new(2026, 8, 20));
        var service = Service(test);

        var posting = await service.PostAsync(
            test.UserId, test.Organisation.Id, project.Id, new(2026, 8, 31));

        Assert.Equal(0m, posting.PreviousWipAmount);
        Assert.Equal(20_000m, posting.RequiredWipAmount);
        Assert.Equal(20_000m, posting.MovementAmount);
        var journal = await test.Db.PostedJournals.AsNoTracking()
            .Include(x => x.Lines)
            .SingleAsync(x => x.Id == posting.PostedJournalId);
        Assert.Equal(20_000m, journal.Lines.Single(
            x => x.LedgerAccountId == test.Account("1100").Id).Debit);
        Assert.Equal(20_000m, journal.Lines.Single(
            x => x.LedgerAccountId == test.Account("4000").Id).Credit);
        Assert.All(journal.Lines, x => Assert.Equal(project.Id, x.ProjectId));

        var recognition = Assert.Single(await new ProjectRevenueRecognitionService(
            test.Db, new ProjectService(test.Db, test.Access)).GetAsync(
                test.UserId, test.Organisation.Id, new(2026, 8, 31)));
        Assert.Equal(30_000m, recognition.PostedRevenue);
        Assert.Equal(20_000m, recognition.PostedWipAmount);
        Assert.Equal(0m, recognition.WipMovementRequired);
        Assert.True(recognition.IsPostedForAsAt);

        var accountChange = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new OrganisationSettingsService(test.Db).UpdateProjectWipAccountsAsync(
                test.UserId,
                new UpdateProjectWipAccountsRequest(
                    test.Organisation.Id,
                    test.Account("1200").Id,
                    test.Account("2000").Id,
                    test.Account("4000").Id)));
        Assert.Contains("cannot be changed", accountChange.Message,
            StringComparison.OrdinalIgnoreCase);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PostAsync(
                test.UserId, test.Organisation.Id, project.Id, new(2026, 8, 31)));
        Assert.Contains("already been posted", exception.Message,
            StringComparison.OrdinalIgnoreCase);

        test.Db.ProjectWipPostings.Remove(posting);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            test.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task PostAsync_TrueUpCanMoveFromContractAssetToContractLiability()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var (branch, division) = await DefaultDimensionAsync(test);
        var project = await CreateActiveProjectAsync(test, division.Id, "JOB-WIP-SWING");
        await ConfigureAccountsAsync(test);
        await PostCostAndRevenueAsync(
            test, branch, division, project, 40_000m, 30_000m, new(2026, 8, 20));
        var service = Service(test);
        await service.PostAsync(
            test.UserId, test.Organisation.Id, project.Id, new(2026, 8, 31));
        await PostRevenueAsync(
            test, branch, division, project, 40_000m, new(2026, 9, 1));

        var posting = await service.PostAsync(
            test.UserId, test.Organisation.Id, project.Id, new(2026, 9, 30));

        Assert.Equal(20_000m, posting.PreviousWipAmount);
        Assert.Equal(-20_000m, posting.RequiredWipAmount);
        Assert.Equal(-40_000m, posting.MovementAmount);
        var journal = await test.Db.PostedJournals.AsNoTracking()
            .Include(x => x.Lines)
            .SingleAsync(x => x.Id == posting.PostedJournalId);
        Assert.Equal(20_000m, journal.Lines.Single(
            x => x.LedgerAccountId == test.Account("1100").Id).Credit);
        Assert.Equal(20_000m, journal.Lines.Single(
            x => x.LedgerAccountId == test.Account("2000").Id).Credit);
        Assert.Equal(40_000m, journal.Lines.Single(
            x => x.LedgerAccountId == test.Account("4000").Id).Debit);

        var recognition = Assert.Single(await new ProjectRevenueRecognitionService(
            test.Db, new ProjectService(test.Db, test.Access)).GetAsync(
                test.UserId, test.Organisation.Id, new(2026, 9, 30)));
        Assert.Equal(70_000m, recognition.PostedRevenue);
        Assert.Equal(-20_000m, recognition.PostedWipAmount);
        Assert.Equal(20_000m, recognition.ContractLiability);
        Assert.Equal(0m, recognition.WipMovementRequired);
    }

    [Fact]
    public async Task PostAsync_RequiresConfiguredAccountsAndChronologicalDates()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var (branch, division) = await DefaultDimensionAsync(test);
        var project = await CreateActiveProjectAsync(test, division.Id, "JOB-WIP-CONTROL");
        await PostCostAndRevenueAsync(
            test, branch, division, project, 40_000m, 30_000m, new(2026, 8, 20));
        var service = Service(test);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.PostAsync(
                Guid.NewGuid().ToString(),
                test.Organisation.Id,
                project.Id,
                new(2026, 9, 30)));

        var missingAccounts = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PostAsync(
                test.UserId, test.Organisation.Id, project.Id, new(2026, 9, 30)));
        Assert.Contains("Configure", missingAccounts.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await test.Db.ProjectWipPostings.ToListAsync());

        await ConfigureAccountsAsync(test);
        await service.PostAsync(
            test.UserId, test.Organisation.Id, project.Id, new(2026, 9, 30));
        var outOfOrder = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PostAsync(
                test.UserId, test.Organisation.Id, project.Id, new(2026, 8, 31)));
        Assert.Contains("later WIP posting", outOfOrder.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private static ProjectWipPostingService Service(AccountingTestDatabase test) =>
        new(
            test.Db,
            new ProjectRevenueRecognitionService(
                test.Db, new ProjectService(test.Db, test.Access)),
            test.Posting,
            test.Access);

    private static async Task ConfigureAccountsAsync(AccountingTestDatabase test) =>
        await new OrganisationSettingsService(test.Db).UpdateProjectWipAccountsAsync(
            test.UserId,
            new UpdateProjectWipAccountsRequest(
                test.Organisation.Id,
                test.Account("1100").Id,
                test.Account("2000").Id,
                test.Account("4000").Id));

    private static async Task<Project> CreateActiveProjectAsync(
        AccountingTestDatabase test,
        Guid divisionId,
        string number)
    {
        var projects = new ProjectService(test.Db, test.Access);
        var project = await projects.SaveAsync(test.UserId, new ProjectRequest(
            test.Organisation.Id,
            null,
            number,
            number,
            null,
            divisionId,
            test.Customer.Id,
            new DateOnly(2026, 8, 1),
            null,
            100_000m,
            0m,
            80_000m,
            0m));
        return await projects.ChangeStatusAsync(
            test.UserId, test.Organisation.Id, project.Id, ProjectStatus.Active);
    }

    private static async Task PostCostAndRevenueAsync(
        AccountingTestDatabase test,
        Branch branch,
        Division division,
        Project project,
        decimal cost,
        decimal revenue,
        DateOnly date)
    {
        await test.Posting.PostAsync(test.UserId, new(
            test.Organisation.Id,
            date,
            $"COST-{project.ProjectNumber}",
            "Project cost",
            [
                new(test.Account("6000").Id, "Project cost", cost, 0m,
                    ProjectId: project.Id),
                new(test.Account("1000").Id, "Cash", 0m, cost)
            ],
            branch.Id,
            division.Id));
        await PostRevenueAsync(test, branch, division, project, revenue, date);
    }

    private static Task PostRevenueAsync(
        AccountingTestDatabase test,
        Branch branch,
        Division division,
        Project project,
        decimal revenue,
        DateOnly date) =>
        test.Posting.PostAsync(test.UserId, new(
            test.Organisation.Id,
            date,
            $"REVENUE-{project.ProjectNumber}-{date:yyyyMMdd}",
            "Project revenue",
            [
                new(test.Account("1100").Id, "Receivable", revenue, 0m),
                new(test.Account("4000").Id, "Project revenue", 0m, revenue,
                    ProjectId: project.Id)
            ],
            branch.Id,
            division.Id));

    private static async Task<(Branch Branch, Division Division)> DefaultDimensionAsync(
        AccountingTestDatabase test)
    {
        var branch = await test.Db.Branches.Include(x => x.Divisions)
            .SingleAsync(x => x.OrganisationId == test.Organisation.Id && x.IsDefault);
        return (branch, branch.Divisions.Single(x => x.IsDefault));
    }
}
