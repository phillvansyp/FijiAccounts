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
}
