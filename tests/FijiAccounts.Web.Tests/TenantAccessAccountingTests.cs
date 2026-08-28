using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class TenantAccessAccountingTests
{
    [Theory]
    [InlineData(OrganisationRole.Owner, true)]
    [InlineData(OrganisationRole.Administrator, true)]
    [InlineData(OrganisationRole.Accountant, false)]
    [InlineData(OrganisationRole.Bookkeeper, false)]
    [InlineData(OrganisationRole.Payroll, false)]
    [InlineData(OrganisationRole.Sales, false)]
    [InlineData(OrganisationRole.ReadOnly, false)]
    [InlineData(OrganisationRole.Approver, false)]
    public async Task CanManageTeam_UsesExpectedDirectMembershipRoles(
        OrganisationRole role,
        bool expected)
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var membership =
            await test.Db.OrganisationMemberships
                .SingleAsync(x =>
                    x.UserId == test.UserId &&
                    x.OrganisationId == test.Organisation.Id);

        membership.Role = role;

        await test.Db.SaveChangesAsync();

        var allowed =
            await test.Access.CanManageTeamAsync(
                test.UserId,
                test.Organisation.Id);

        Assert.Equal(expected, allowed);
    }

    [Theory]
    [InlineData(OrganisationRole.Owner, true)]
    [InlineData(OrganisationRole.Administrator, true)]
    [InlineData(OrganisationRole.Accountant, true)]
    [InlineData(OrganisationRole.Bookkeeper, true)]
    [InlineData(OrganisationRole.Payroll, false)]
    [InlineData(OrganisationRole.Sales, false)]
    [InlineData(OrganisationRole.ReadOnly, false)]
    [InlineData(OrganisationRole.Approver, false)]
    public async Task CanPostJournals_UsesExpectedDirectMembershipRoles(
        OrganisationRole role,
        bool expected)
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var membership =
            await test.Db.OrganisationMemberships
                .SingleAsync(x =>
                    x.UserId == test.UserId &&
                    x.OrganisationId == test.Organisation.Id);

        membership.Role = role;

        await test.Db.SaveChangesAsync();

        var allowed =
            await test.Access.CanPostJournalsAsync(
                test.UserId,
                test.Organisation.Id);

        Assert.Equal(expected, allowed);
    }

    [Theory]
    [InlineData(OrganisationRole.Owner, true)]
    [InlineData(OrganisationRole.Administrator, true)]
    [InlineData(OrganisationRole.Accountant, true)]
    [InlineData(OrganisationRole.Bookkeeper, true)]
    [InlineData(OrganisationRole.Payroll, false)]
    [InlineData(OrganisationRole.Sales, false)]
    [InlineData(OrganisationRole.ReadOnly, false)]
    [InlineData(OrganisationRole.Approver, false)]
    public async Task CanManageContacts_UsesExpectedDirectMembershipRoles(
        OrganisationRole role,
        bool expected)
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var membership = await test.Db.OrganisationMemberships.SingleAsync(x =>
            x.UserId == test.UserId &&
            x.OrganisationId == test.Organisation.Id);
        membership.Role = role;
        await test.Db.SaveChangesAsync();

        var allowed = await test.Access.CanManageContactsAsync(
            test.UserId,
            test.Organisation.Id);

        Assert.Equal(expected, allowed);
    }

    [Theory]
    [InlineData(OrganisationRole.Owner, true, true)]
    [InlineData(OrganisationRole.Administrator, true, false)]
    [InlineData(OrganisationRole.Accountant, false, false)]
    [InlineData(OrganisationRole.Bookkeeper, false, false)]
    [InlineData(OrganisationRole.Payroll, false, false)]
    [InlineData(OrganisationRole.Sales, false, false)]
    [InlineData(OrganisationRole.ReadOnly, false, false)]
    [InlineData(OrganisationRole.Approver, true, false)]
    public async Task PurchaseApproval_UsesExpectedDirectMembershipRoles(
        OrganisationRole role,
        bool canApproveOwnerOrAdministrator,
        bool canApproveOwnerOnly)
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var membership = await test.Db.OrganisationMemberships.SingleAsync(x =>
            x.UserId == test.UserId &&
            x.OrganisationId == test.Organisation.Id);
        membership.Role = role;
        await test.Db.SaveChangesAsync();
        var approvals = new PurchaseApprovalPolicyService(test.Db, test.Access);

        Assert.Equal(
            canApproveOwnerOrAdministrator,
            await approvals.CanApproveAsync(
                test.UserId,
                test.Organisation.Id,
                PurchaseApprovalRequirement.OwnerOrAdministrator));
        Assert.Equal(
            canApproveOwnerOnly,
            await approvals.CanApproveAsync(
                test.UserId,
                test.Organisation.Id,
                PurchaseApprovalRequirement.OwnerOnly));
    }

    [Theory]
    [InlineData(EngagementAccess.ReadOnly, false)]
    [InlineData(EngagementAccess.Bookkeeping, true)]
    [InlineData(EngagementAccess.Accountant, true)]
    [InlineData(EngagementAccess.Full, true)]
    public async Task CanPostJournals_UsesExpectedPracticeEngagementAccess(
        EngagementAccess engagementAccess,
        bool expected)
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var practice =
            await AddOrganisationAsync(
                test,
                "Test Accounting Practice",
                OrganisationKind.AccountingPractice);

        var client =
            await AddOrganisationAsync(
                test,
                "Practice Client Limited",
                OrganisationKind.Business);

        test.Db.OrganisationMemberships.Add(
            new OrganisationMembership
            {
                OrganisationId = practice.Id,
                Organisation = practice,
                UserId = test.UserId,
                Role = OrganisationRole.Accountant
            });

        test.Db.AccountantEngagements.Add(
            new AccountantEngagement
            {
                PracticeOrganisationId = practice.Id,
                PracticeOrganisation = practice,
                ClientOrganisationId = client.Id,
                ClientOrganisation = client,
                Access = engagementAccess
            });

        await test.Db.SaveChangesAsync();

        var allowed =
            await test.Access.CanPostJournalsAsync(
                test.UserId,
                client.Id);

        Assert.Equal(expected, allowed);
    }

    [Fact]
    public async Task CanPostJournals_RevokedPracticeEngagement_DeniesAccess()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var practice =
            await AddOrganisationAsync(
                test,
                "Revoked Access Practice",
                OrganisationKind.AccountingPractice);

        var client =
            await AddOrganisationAsync(
                test,
                "Revoked Access Client",
                OrganisationKind.Business);

        test.Db.OrganisationMemberships.Add(
            new OrganisationMembership
            {
                OrganisationId = practice.Id,
                Organisation = practice,
                UserId = test.UserId,
                Role = OrganisationRole.Owner
            });

        test.Db.AccountantEngagements.Add(
            new AccountantEngagement
            {
                PracticeOrganisationId = practice.Id,
                PracticeOrganisation = practice,
                ClientOrganisationId = client.Id,
                ClientOrganisation = client,
                Access = EngagementAccess.Full,
                RevokedAt = DateTimeOffset.UtcNow
            });

        await test.Db.SaveChangesAsync();

        var allowed =
            await test.Access.CanPostJournalsAsync(
                test.UserId,
                client.Id);

        Assert.False(allowed);
    }

    [Theory]
    [InlineData(OrganisationRole.Payroll)]
    [InlineData(OrganisationRole.Sales)]
    [InlineData(OrganisationRole.ReadOnly)]
    [InlineData(OrganisationRole.Approver)]
    public async Task CanPostJournals_NonAccountingPracticeRole_DeniesClientAccess(
        OrganisationRole role)
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var practice =
            await AddOrganisationAsync(
                test,
                "Restricted Practice",
                OrganisationKind.AccountingPractice);

        var client =
            await AddOrganisationAsync(
                test,
                "Restricted Client",
                OrganisationKind.Business);

        test.Db.OrganisationMemberships.Add(
            new OrganisationMembership
            {
                OrganisationId = practice.Id,
                Organisation = practice,
                UserId = test.UserId,
                Role = role
            });

        test.Db.AccountantEngagements.Add(
            new AccountantEngagement
            {
                PracticeOrganisationId = practice.Id,
                PracticeOrganisation = practice,
                ClientOrganisationId = client.Id,
                ClientOrganisation = client,
                Access = EngagementAccess.Full
            });

        await test.Db.SaveChangesAsync();

        var allowed =
            await test.Access.CanPostJournalsAsync(
                test.UserId,
                client.Id);

        Assert.False(allowed);
    }

    [Fact]
    public async Task List_ReturnsDirectAndEngagedOrganisations_SortedByLegalName()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var practice =
            await AddOrganisationAsync(
                test,
                "Zulu Accounting Practice",
                OrganisationKind.AccountingPractice);

        var client =
            await AddOrganisationAsync(
                test,
                "Alpha Client Limited",
                OrganisationKind.Business);

        test.Db.OrganisationMemberships.Add(
            new OrganisationMembership
            {
                OrganisationId = practice.Id,
                Organisation = practice,
                UserId = test.UserId,
                Role = OrganisationRole.Bookkeeper
            });

        test.Db.AccountantEngagements.Add(
            new AccountantEngagement
            {
                PracticeOrganisationId = practice.Id,
                PracticeOrganisation = practice,
                ClientOrganisationId = client.Id,
                ClientOrganisation = client,
                Access = EngagementAccess.Bookkeeping
            });

        await test.Db.SaveChangesAsync();

        var organisations =
            await test.Access.ListAsync(test.UserId);

        Assert.Equal(3, organisations.Count);

        Assert.Equal(
            new[]
            {
                "Accounting Test Limited",
                "Alpha Client Limited",
                "Zulu Accounting Practice"
            },
            organisations
                .Select(x => x.Organisation.LegalName)
                .ToArray());

        var direct =
            organisations.Single(x =>
                x.Organisation.Id == test.Organisation.Id);

        Assert.False(direct.IsClient);
        Assert.Equal("Owner", direct.AccessLabel);

        var engagedClient =
            organisations.Single(x =>
                x.Organisation.Id == client.Id);

        Assert.True(engagedClient.IsClient);
        Assert.Equal(
            EngagementAccess.Bookkeeping.ToString(),
            engagedClient.AccessLabel);
    }

    [Fact]
    public async Task List_RevokedEngagement_DoesNotReturnClient()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var practice =
            await AddOrganisationAsync(
                test,
                "Revoked List Practice",
                OrganisationKind.AccountingPractice);

        var client =
            await AddOrganisationAsync(
                test,
                "Revoked List Client",
                OrganisationKind.Business);

        test.Db.OrganisationMemberships.Add(
            new OrganisationMembership
            {
                OrganisationId = practice.Id,
                Organisation = practice,
                UserId = test.UserId,
                Role = OrganisationRole.Owner
            });

        test.Db.AccountantEngagements.Add(
            new AccountantEngagement
            {
                PracticeOrganisationId = practice.Id,
                PracticeOrganisation = practice,
                ClientOrganisationId = client.Id,
                ClientOrganisation = client,
                Access = EngagementAccess.Full,
                RevokedAt = DateTimeOffset.UtcNow
            });

        await test.Db.SaveChangesAsync();

        var organisations =
            await test.Access.ListAsync(test.UserId);

        Assert.DoesNotContain(
            organisations,
            x => x.Organisation.Id == client.Id);
    }

    [Fact]
    public async Task List_DirectAndEngagedAccessToSameOrganisation_ReturnsOnce()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var practice =
            await AddOrganisationAsync(
                test,
                "Duplicate Access Practice",
                OrganisationKind.AccountingPractice);

        var client =
            await AddOrganisationAsync(
                test,
                "Duplicate Access Client",
                OrganisationKind.Business);

        test.Db.OrganisationMemberships.AddRange(
            new OrganisationMembership
            {
                OrganisationId = practice.Id,
                Organisation = practice,
                UserId = test.UserId,
                Role = OrganisationRole.Accountant
            },
            new OrganisationMembership
            {
                OrganisationId = client.Id,
                Organisation = client,
                UserId = test.UserId,
                Role = OrganisationRole.Bookkeeper
            });

        test.Db.AccountantEngagements.Add(
            new AccountantEngagement
            {
                PracticeOrganisationId = practice.Id,
                PracticeOrganisation = practice,
                ClientOrganisationId = client.Id,
                ClientOrganisation = client,
                Access = EngagementAccess.Accountant
            });

        await test.Db.SaveChangesAsync();

        var organisations =
            await test.Access.ListAsync(test.UserId);

        Assert.Single(
    organisations,
    x => x.Organisation.Id == client.Id);
    }

    [Fact]
    public async Task Find_ReturnsAccessibleOrganisation_AndNullForInaccessibleOrganisation()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var accessible =
            await test.Access.FindAsync(
                test.UserId,
                test.Organisation.Id);

        Assert.NotNull(accessible);
        Assert.Equal(
            test.Organisation.Id,
            accessible.Organisation.Id);

        var inaccessibleOrganisation =
            await AddOrganisationAsync(
                test,
                "Inaccessible Organisation",
                OrganisationKind.Business);

        var inaccessible =
            await test.Access.FindAsync(
                test.UserId,
                inaccessibleOrganisation.Id);

        Assert.Null(inaccessible);
    }

    [Fact]
    public async Task CustomPermissionProfile_OverridesStandardRolePermissions()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var profile = new OrganisationPermissionProfile
        {
            OrganisationId = test.Organisation.Id,
            Name = "Operations lead",
            CanManageTeam = true,
            CanPostAccounting = true,
            CanManageContacts = true,
            CanApprovePurchases = true,
            CreatedByUserId = test.UserId
        };
        test.Db.OrganisationPermissionProfiles.Add(profile);
        var membership = await test.Db.OrganisationMemberships.SingleAsync(x =>
            x.UserId == test.UserId && x.OrganisationId == test.Organisation.Id);
        membership.Role = OrganisationRole.ReadOnly;
        membership.PermissionProfile = profile;
        await test.Db.SaveChangesAsync();
        var approvals = new PurchaseApprovalPolicyService(test.Db, test.Access);

        Assert.True(await test.Access.CanManageTeamAsync(test.UserId, test.Organisation.Id));
        Assert.True(await test.Access.CanPostJournalsAsync(test.UserId, test.Organisation.Id));
        Assert.True(await test.Access.CanManageContactsAsync(test.UserId, test.Organisation.Id));
        Assert.True(await approvals.CanApproveAsync(test.UserId, test.Organisation.Id, PurchaseApprovalRequirement.OwnerOrAdministrator));
        Assert.False(await approvals.CanApproveAsync(test.UserId, test.Organisation.Id, PurchaseApprovalRequirement.OwnerOnly));
    }

    [Fact]
    public async Task CustomPermissionProfile_CanRemoveAdministratorTeamManagement()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var profile = new OrganisationPermissionProfile
        {
            OrganisationId = test.Organisation.Id,
            Name = "Restricted administrator",
            CreatedByUserId = test.UserId
        };
        test.Db.OrganisationPermissionProfiles.Add(profile);
        var membership = await test.Db.OrganisationMemberships.SingleAsync(x =>
            x.UserId == test.UserId && x.OrganisationId == test.Organisation.Id);
        membership.Role = OrganisationRole.Administrator;
        membership.PermissionProfile = profile;
        await test.Db.SaveChangesAsync();

        Assert.False(await test.Access.CanManageTeamAsync(test.UserId, test.Organisation.Id));
        Assert.False(await test.Access.CanPostJournalsAsync(test.UserId, test.Organisation.Id));
        Assert.False(await test.Access.CanManageContactsAsync(test.UserId, test.Organisation.Id));
    }

    private static async Task<Organisation> AddOrganisationAsync(
        AccountingTestDatabase test,
        string legalName,
        OrganisationKind kind)
    {
        var organisation =
            new Organisation
            {
                LegalName = legalName,
                CountryCode = "FJ",
                BaseCurrency = "FJD",
                TaxLabel = "VAT",
                Kind = kind
            };

        test.Db.Organisations.Add(organisation);

        await test.Db.SaveChangesAsync();

        return organisation;
    }
}
