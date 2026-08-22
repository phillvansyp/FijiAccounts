using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class EnterpriseStructureServiceTests
{
    [Fact]
    public async Task AddDefaultFor_CreatesPersistedGroupBranchAndDivision()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var group =
            await test.Db.OrganisationGroups
                .AsNoTracking()
                .Include(x => x.Companies)
                .SingleAsync();

        var branch =
            await test.Db.Branches
                .AsNoTracking()
                .Include(x => x.Divisions)
                .SingleAsync();

        Assert.Equal(
            "Accounting Test Limited Group",
            group.Name);
        Assert.Equal(test.Organisation.Id, group.Companies.Single().Id);
        Assert.Equal(test.Organisation.Id, branch.OrganisationId);
        Assert.Equal("MAIN", branch.Code);
        Assert.Equal("Main Branch", branch.Name);
        Assert.True(branch.IsDefault);

        var division = branch.Divisions.Single();

        Assert.Equal("GENERAL", division.Code);
        Assert.Equal("General", division.Name);
        Assert.True(division.IsDefault);
    }

    [Fact]
    public async Task AddDefaultFor_WhenCompanyAlreadyBelongsToGroup_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var exception =
            Assert.Throws<InvalidOperationException>(() =>
                new EnterpriseStructureService(test.Db)
                    .AddDefaultFor(test.Organisation));

        Assert.Contains(
            "already belongs",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GroupRelationship_DoesNotBroadenCompanyTenantAccess()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var secondCompany =
            new Organisation
            {
                LegalName = "Second Company Limited",
                CountryCode = "FJ",
                BaseCurrency = "FJD",
                Kind = OrganisationKind.Business,
                OrganisationGroupId = test.Organisation.OrganisationGroupId
            };

        test.Db.Organisations.Add(secondCompany);
        await test.Db.SaveChangesAsync();

        var accessible =
            await test.Access.ListAsync(test.UserId);

        Assert.Contains(
            accessible,
            x => x.Organisation.Id == test.Organisation.Id);
        Assert.DoesNotContain(
            accessible,
            x => x.Organisation.Id == secondCompany.Id);
    }

    [Fact]
    public async Task BranchAndDivisionManagement_PreservesDefaultsAndStatuses()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new EnterpriseStructureService(test.Db);

        var branch =
            await service.AddBranchAsync(
                test.Organisation.Id,
                "nadi",
                "Nadi Branch");

        var division =
            await service.AddDivisionAsync(
                test.Organisation.Id,
                branch.Id,
                "retail",
                "Retail");

        await service.ToggleBranchAsync(
            test.Organisation.Id,
            branch.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddDivisionAsync(
                test.Organisation.Id,
                branch.Id,
                "CLOSED",
                "Closed"));

        await service.ToggleDivisionAsync(
            test.Organisation.Id,
            division.Id);

        var branches =
            await service.ListBranchesAsync(
                test.Organisation.Id);

        var storedBranch =
            branches.Single(x => x.Id == branch.Id);

        Assert.Equal("NADI", storedBranch.Code);
        Assert.False(storedBranch.IsActive);
        Assert.Contains(
            storedBranch.Divisions,
            x =>
                x.Code == "GENERAL" &&
                x.IsDefault &&
                x.IsActive);
        Assert.Contains(
            storedBranch.Divisions,
            x =>
                x.Code == "RETAIL" &&
                !x.IsDefault &&
                !x.IsActive);
    }

    [Fact]
    public async Task HierarchyManagement_RejectsCrossCompanyAndDefaultDeactivation()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new EnterpriseStructureService(test.Db);

        var defaultBranch =
            (await service.ListBranchesAsync(test.Organisation.Id))
                .Single(x => x.IsDefault);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ToggleBranchAsync(
                test.Organisation.Id,
                defaultBranch.Id));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddDivisionAsync(
                Guid.NewGuid(),
                defaultBranch.Id,
                "OTHER",
                "Other"));
    }
}
