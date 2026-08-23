using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FijiAccounts.Web.Tests;

public sealed class EnterpriseStructureServiceTests
{
    [Fact]
    public async Task CreateStandaloneCompany_ProvisionsCompleteAuditedWorkspace()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new EnterpriseStructureService(test.Db);

        var company = await service.CreateStandaloneCompanyAsync(
            test.UserId,
            new CreateStandaloneCompanyRequest(
                " New Company Limited ",
                " New Company ",
                " TIN-NEW ",
                "FJ",
                OrganisationKind.Business));

        var stored = await test.Db.Organisations
            .AsNoTracking()
            .SingleAsync(x => x.Id == company.Id);
        var membership = await test.Db.OrganisationMemberships
            .AsNoTracking()
            .SingleAsync(x => x.OrganisationId == company.Id);
        var branch = await test.Db.Branches
            .AsNoTracking()
            .Include(x => x.Divisions)
            .SingleAsync(x => x.OrganisationId == company.Id);
        var groupMembership = await test.Db.OrganisationGroupMemberships
            .AsNoTracking()
            .SingleAsync(x => x.OrganisationGroupId == stored.OrganisationGroupId);
        var audit = await test.Db.AuditEvents
            .AsNoTracking()
            .SingleAsync(x =>
                x.OrganisationId == company.Id &&
                x.EventType == "OrganisationCreated");

        Assert.Equal("New Company Limited", stored.LegalName);
        Assert.Equal("New Company", stored.TradingName);
        Assert.Equal("TIN-NEW", stored.Tin);
        Assert.Equal(test.UserId, membership.UserId);
        Assert.Equal(OrganisationRole.Owner, membership.Role);
        Assert.Equal(test.UserId, groupMembership.UserId);
        Assert.Equal(OrganisationGroupRole.Owner, groupMembership.Role);
        Assert.Equal("MAIN", branch.Code);
        Assert.Equal("GENERAL", Assert.Single(branch.Divisions).Code);
        Assert.Equal(
            FijiStarterChart.For(company.Id).Count,
            await test.Db.LedgerAccounts.CountAsync(x => x.OrganisationId == company.Id));
        Assert.Equal(test.UserId, audit.UserId);
        using var evidence = JsonDocument.Parse(audit.JsonData);
        Assert.Equal(
            branch.Id.ToString(),
            evidence.RootElement.GetProperty("DefaultBranchId").GetString());
    }

    [Fact]
    public async Task CreateStandaloneCompany_RejectsUnknownUserWithoutPartialWorkspace()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new EnterpriseStructureService(test.Db);
        var initialOrganisationCount = await test.Db.Organisations.CountAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CreateStandaloneCompanyAsync(
                Guid.NewGuid().ToString(),
                new CreateStandaloneCompanyRequest(
                    "Unauthorised Limited",
                    null,
                    null,
                    "FJ",
                    OrganisationKind.Business)));

        Assert.Equal(initialOrganisationCount, await test.Db.Organisations.CountAsync());
        Assert.DoesNotContain(
            await test.Db.AuditEvents.AsNoTracking().ToListAsync(),
            x => x.EventType == "OrganisationCreated");
    }

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
                test.UserId,
                test.Organisation.Id,
                "nadi",
                "Nadi Branch");

        var division =
            await service.AddDivisionAsync(
                test.UserId,
                test.Organisation.Id,
                branch.Id,
                "retail",
                "Retail");

        await service.ToggleBranchAsync(
            test.UserId,
            test.Organisation.Id,
            branch.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddDivisionAsync(
                test.UserId,
                test.Organisation.Id,
                branch.Id,
                "CLOSED",
                "Closed"));

        await service.ToggleDivisionAsync(
            test.UserId,
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
                test.UserId,
                test.Organisation.Id,
                defaultBranch.Id));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.AddDivisionAsync(
                test.UserId,
                Guid.NewGuid(),
                defaultBranch.Id,
                "OTHER",
                "Other"));
    }

    [Fact]
    public async Task HierarchyManagement_RejectsNonManagerServiceCalls()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();
        var service = new EnterpriseStructureService(test.Db);
        var branch =
            await service.AddBranchAsync(
                test.UserId,
                test.Organisation.Id,
                "NADI",
                "Nadi Branch");
        var division =
            await service.AddDivisionAsync(
                test.UserId,
                test.Organisation.Id,
                branch.Id,
                "RETAIL",
                "Retail");
        var accountant = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "accountant@example.com",
            NormalizedUserName = "ACCOUNTANT@EXAMPLE.COM"
        };

        test.Db.Users.Add(accountant);
        test.Db.OrganisationMemberships.Add(
            new OrganisationMembership
            {
                OrganisationId = test.Organisation.Id,
                UserId = accountant.Id,
                Role = OrganisationRole.Accountant
            });
        await test.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.AddBranchAsync(
                accountant.Id,
                test.Organisation.Id,
                "SUVA",
                "Suva Branch"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.AddDivisionAsync(
                accountant.Id,
                test.Organisation.Id,
                branch.Id,
                "WHOLESALE",
                "Wholesale"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ToggleBranchAsync(
                accountant.Id,
                test.Organisation.Id,
                branch.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ToggleDivisionAsync(
                accountant.Id,
                test.Organisation.Id,
                division.Id));
    }

    [Fact]
    public async Task GroupOwner_CanRenameGroupAndCreateIsolatedCompany()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();
        var service = new EnterpriseStructureService(test.Db);

        await service.UpdateGroupNameAsync(
            test.UserId,
            test.Organisation.Id,
            "Downer Group");
        var company =
            await service.AddCompanyAsync(
                test.UserId,
                new CreateGroupCompanyRequest(
                    test.Organisation.Id,
                    "Higgins Limited",
                    "Higgins",
                    "TIN-2",
                    "FJ",
                    OrganisationKind.Business));

        var group =
            await service.GetGroupAsync(
                test.UserId,
                test.Organisation.Id);
        var companyMembership =
            await test.Db.OrganisationMemberships
                .AsNoTracking()
                .SingleAsync(x => x.OrganisationId == company.Id);
        var branch =
            await test.Db.Branches
                .AsNoTracking()
                .Include(x => x.Divisions)
                .SingleAsync(x => x.OrganisationId == company.Id);
        var accountCount =
            await test.Db.LedgerAccounts.CountAsync(
                x => x.OrganisationId == company.Id);

        Assert.NotNull(group);
        Assert.Equal("Downer Group", group.Name);
        Assert.Equal(2, group.Companies.Count);
        Assert.Equal(test.Organisation.OrganisationGroupId, company.OrganisationGroupId);
        Assert.Equal(test.UserId, companyMembership.UserId);
        Assert.Equal(OrganisationRole.Owner, companyMembership.Role);
        Assert.Equal("MAIN", branch.Code);
        Assert.Equal("GENERAL", branch.Divisions.Single().Code);
        Assert.Equal(FijiStarterChart.For(company.Id).Count, accountCount);
    }

    [Fact]
    public async Task LegacyOwnerWithoutGroupMembership_CanManageGroup()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();
        var service = new EnterpriseStructureService(test.Db);

        await test.Db.OrganisationGroupMemberships
            .Where(x => x.UserId == test.UserId)
            .ExecuteDeleteAsync();

        var group =
            await service.GetGroupAsync(
                test.UserId,
                test.Organisation.Id);
        await service.UpdateGroupNameAsync(
            test.UserId,
            test.Organisation.Id,
            "Legacy Owner Group");
        var company =
            await service.AddCompanyAsync(
                test.UserId,
                new CreateGroupCompanyRequest(
                    test.Organisation.Id,
                    "Legacy Subsidiary Limited",
                    null,
                    null,
                    "FJ",
                    OrganisationKind.Business));

        Assert.NotNull(group);
        Assert.Equal(OrganisationGroupRole.Owner, group.Role);
        Assert.Equal(
            "Legacy Owner Group",
            await test.Db.OrganisationGroups
                .Select(x => x.Name)
                .SingleAsync());
        Assert.Equal(
            OrganisationRole.Owner,
            await test.Db.OrganisationMemberships
                .Where(x => x.OrganisationId == company.Id)
                .Select(x => x.Role)
                .SingleAsync());
    }

    [Fact]
    public async Task ExplicitGroupViewer_IsNotElevatedByCompanyOwnership()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();
        var service = new EnterpriseStructureService(test.Db);

        await test.Db.OrganisationGroupMemberships
            .Where(x => x.UserId == test.UserId)
            .ExecuteUpdateAsync(update =>
                update.SetProperty(
                    x => x.Role,
                    OrganisationGroupRole.Viewer));

        var group =
            await service.GetGroupAsync(
                test.UserId,
                test.Organisation.Id);

        Assert.NotNull(group);
        Assert.Equal(OrganisationGroupRole.Viewer, group.Role);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.UpdateGroupNameAsync(
                test.UserId,
                test.Organisation.Id,
                "Not Allowed"));
    }

    [Fact]
    public async Task GroupManagement_RejectsUserWithoutGroupRole()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();
        var service = new EnterpriseStructureService(test.Db);
        var otherUserId = Guid.NewGuid().ToString();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.UpdateGroupNameAsync(
                otherUserId,
                test.Organisation.Id,
                "Unauthorised rename"));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.AddCompanyAsync(
                otherUserId,
                new CreateGroupCompanyRequest(
                    test.Organisation.Id,
                    "Unauthorised Company",
                    null,
                    null,
                    "FJ",
                    OrganisationKind.Business)));
    }
}
