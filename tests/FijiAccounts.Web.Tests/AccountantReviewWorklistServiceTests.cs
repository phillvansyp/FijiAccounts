using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class AccountantReviewWorklistServiceTests
{
    [Fact]
    public async Task GetAsync_ShowsManagersAllQueriesAndAssigneesOnlyTheirOwn()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var period = await AddPeriodAsync(test);
        var assigned = await AddMemberAsync(test, "assigned-review@example.com");
        var other = await AddMemberAsync(test, "other-review@example.com");
        var reviews = new YearEndReviewService(test.Db, test.Access);
        await reviews.StartAsync(test.UserId, test.Organisation.Id, period.Id);
        await reviews.UpdateItemAsync(
            test.UserId,
            test.Organisation.Id,
            period.Id,
            YearEndReviewArea.AgedPayables,
            YearEndReviewStatus.QueryRaised,
            "Reconcile the supplier statement.",
            assigned.Id,
            DateOnly.FromDateTime(DateTime.Today).AddDays(-1));
        var service = new AccountantReviewWorklistService(test.Db, test.Access);

        var managerWork = await service.GetAsync(test.UserId);
        var managerItem = Assert.Single(managerWork);
        Assert.True(managerItem.CanManage);
        Assert.True(managerItem.IsOverdue(DateOnly.FromDateTime(DateTime.Today)));

        var assignedWork = await service.GetAsync(assigned.Id);
        var assignedItem = Assert.Single(assignedWork);
        Assert.True(assignedItem.IsAssignedToCurrentUser);
        Assert.False(assignedItem.CanManage);
        Assert.Empty(await service.GetAsync(other.Id));

        await reviews.RespondAsync(
            assigned.Id,
            test.Organisation.Id,
            period.Id,
            YearEndReviewArea.AgedPayables,
            "Statement reconciled and evidence uploaded.");
        Assert.True(Assert.Single(await service.GetAsync(assigned.Id)).HasResponse);
        await reviews.ResolveQueryAsync(
            test.UserId,
            test.Organisation.Id,
            period.Id,
            YearEndReviewArea.AgedPayables);
        Assert.True(Assert.Single(await service.GetAsync(assigned.Id)).IsResolved);
    }

    [Fact]
    public async Task GetAsync_IncludesAssignedQueriesThroughActivePracticeEngagement()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var period = await AddPeriodAsync(test);
        var accountant = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "practice-accountant@example.com",
            NormalizedUserName = "PRACTICE-ACCOUNTANT@EXAMPLE.COM",
            Email = "practice-accountant@example.com",
            NormalizedEmail = "PRACTICE-ACCOUNTANT@EXAMPLE.COM",
            EmailConfirmed = true
        };
        var practice = new Organisation
        {
            LegalName = "Island Accounting Practice",
            Kind = OrganisationKind.AccountingPractice
        };
        test.Db.Users.Add(accountant);
        test.Db.Organisations.Add(practice);
        test.Db.OrganisationMemberships.Add(new OrganisationMembership
        {
            OrganisationId = practice.Id,
            Organisation = practice,
            UserId = accountant.Id,
            User = accountant,
            Role = OrganisationRole.Accountant
        });
        test.Db.AccountantEngagements.Add(new AccountantEngagement
        {
            PracticeOrganisationId = practice.Id,
            PracticeOrganisation = practice,
            ClientOrganisationId = test.Organisation.Id,
            ClientOrganisation = test.Organisation,
            Access = EngagementAccess.Accountant
        });
        await test.Db.SaveChangesAsync();
        var reviews = new YearEndReviewService(test.Db, test.Access);
        await reviews.StartAsync(test.UserId, test.Organisation.Id, period.Id);
        await reviews.UpdateItemAsync(
            test.UserId,
            test.Organisation.Id,
            period.Id,
            YearEndReviewArea.TrialBalance,
            YearEndReviewStatus.QueryRaised,
            "Confirm the suspense account clearance.",
            accountant.Id,
            DateOnly.FromDateTime(DateTime.Today).AddDays(3));

        var item = Assert.Single(await new AccountantReviewWorklistService(
            test.Db,
            test.Access).GetAsync(accountant.Id));
        Assert.Equal(test.Organisation.Id, item.OrganisationId);
        Assert.True(item.IsAssignedToCurrentUser);
        Assert.False(item.CanManage);
    }

    private static async Task<AccountingPeriod> AddPeriodAsync(AccountingTestDatabase test)
    {
        var period = new AccountingPeriod
        {
            OrganisationId = test.Organisation.Id,
            Name = "Year ended 31 July 2026",
            StartsOn = new DateOnly(2025, 8, 1),
            EndsOn = new DateOnly(2026, 7, 31)
        };
        test.Db.AccountingPeriods.Add(period);
        await test.Db.SaveChangesAsync();
        return period;
    }

    private static async Task<ApplicationUser> AddMemberAsync(
        AccountingTestDatabase test,
        string email)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true
        };
        test.Db.Users.Add(user);
        test.Db.OrganisationMemberships.Add(new OrganisationMembership
        {
            OrganisationId = test.Organisation.Id,
            UserId = user.Id,
            User = user,
            Role = OrganisationRole.Bookkeeper
        });
        await test.Db.SaveChangesAsync();
        return user;
    }
}
