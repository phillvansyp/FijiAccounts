using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class OrganisationReplacementServiceTests
{
    [Fact]
    public async Task Apply_ReplacesOnlyGuardedNzWorkspaceAndPreservesSameNamedFijiCompany()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.LegalName = "JCR 2006 Limited";
        test.Organisation.TradingName = "JCR 2006 Limited NZ";
        test.Organisation.CountryCode = "NZ";
        test.Organisation.BaseCurrency = "NZD";
        test.Organisation.TimeZoneId = "Pacific/Auckland";
        test.Organisation.TaxLabel = "GST";
        var user = await test.Db.Users.SingleAsync(x => x.Id == test.UserId);
        user.LastSelectedOrganisationId = test.Organisation.Id;

        var fiji = await new EnterpriseStructureService(test.Db).CreateStandaloneCompanyAsync(
            test.UserId,
            new CreateStandaloneCompanyRequest(
                "JCR 2006 Limited",
                "JCR 2006 Limited Fiji",
                null,
                "FJ",
                OrganisationKind.Business));
        test.Organisation.OrganisationGroupId = fiji.OrganisationGroupId;
        test.Db.XeroConnections.Add(new XeroConnection
        {
            OrganisationId = test.Organisation.Id,
            ProtectedAccessToken = "old-access",
            ProtectedRefreshToken = "old-refresh",
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            GrantedScopes = "accounting.transactions",
            ConnectedByUserId = test.UserId,
            UpdatedByUserId = test.UserId,
            XeroTenantName = "Old NZ Xero tenant"
        });
        await test.Db.SaveChangesAsync();

        var oldId = test.Organisation.Id;
        var oldWorkspaceId = OrganisationDisplay.WorkspaceId(test.Organisation);
        var fijiWorkspaceId = OrganisationDisplay.WorkspaceId(fiji);
        var plan = new ReplaceOrganisationPlan(
            oldWorkspaceId,
            "JCR 2006 Limited",
            "NZ",
            fijiWorkspaceId,
            "JCR 2006 Limited",
            "FJ");
        var service = new OrganisationReplacementService(test.Db);

        var preview = await service.RunAsync(plan, false);
        Assert.False(preview.Applied);
        Assert.Equal(oldWorkspaceId, preview.RemovedWorkspaceId);
        Assert.True(preview.RemovedRows[nameof(ApplicationDbContext.XeroConnections)] > 0);

        var result = await service.RunAsync(plan, true);
        test.Db.ChangeTracker.Clear();

        Assert.True(result.Applied);
        Assert.Equal(oldWorkspaceId, result.RemovedWorkspaceId);
        Assert.Equal(fijiWorkspaceId, result.PreservedWorkspaceId);
        Assert.NotEqual(oldWorkspaceId, result.ReplacementWorkspaceId);
        Assert.False(await test.Db.Organisations.AnyAsync(x => x.Id == oldId));
        Assert.False(await test.Db.XeroConnections.AnyAsync(x => x.OrganisationId == oldId));
        Assert.False(await test.Db.BusinessParties.AnyAsync(x => x.OrganisationId == oldId));

        var preserved = await test.Db.Organisations.AsNoTracking().SingleAsync(x => x.Id == fiji.Id);
        Assert.Equal("FJ", preserved.CountryCode);
        Assert.Equal("FJD", preserved.BaseCurrency);
        Assert.Equal("JCR 2006 Limited", preserved.LegalName);

        var replacement = await test.Db.Organisations.AsNoTracking().SingleAsync(x =>
            x.Id != fiji.Id && x.LegalName == "JCR 2006 Limited");
        Assert.Equal("NZ", replacement.CountryCode);
        Assert.Equal("NZD", replacement.BaseCurrency);
        Assert.NotEqual(fiji.OrganisationGroupId, replacement.OrganisationGroupId);
        Assert.Equal(result.ReplacementWorkspaceId, OrganisationDisplay.WorkspaceId(replacement));
        Assert.Equal(
            OrganisationRole.Owner,
            (await test.Db.OrganisationMemberships.AsNoTracking().SingleAsync(x =>
                x.OrganisationId == replacement.Id && x.UserId == test.UserId)).Role);
        Assert.Equal(
            replacement.Id,
            (await test.Db.Users.AsNoTracking().SingleAsync(x => x.Id == test.UserId)).LastSelectedOrganisationId);
        Assert.Empty(await ForeignKeyViolationsAsync(test.Db));
    }

    [Fact]
    public async Task Apply_RejectsWrongCountryGuardWithoutChangingEitherCompany()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.LegalName = "JCR 2006 Limited";
        test.Organisation.CountryCode = "NZ";
        test.Organisation.BaseCurrency = "NZD";
        var fiji = await new EnterpriseStructureService(test.Db).CreateStandaloneCompanyAsync(
            test.UserId,
            new CreateStandaloneCompanyRequest(
                "JCR 2006 Limited", null, null, "FJ", OrganisationKind.Business));
        await test.Db.SaveChangesAsync();

        var service = new OrganisationReplacementService(test.Db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(
            new ReplaceOrganisationPlan(
                OrganisationDisplay.WorkspaceId(test.Organisation),
                "JCR 2006 Limited",
                "FJ",
                OrganisationDisplay.WorkspaceId(fiji),
                "JCR 2006 Limited",
                "FJ"),
            true));

        Assert.True(await test.Db.Organisations.AnyAsync(x => x.Id == test.Organisation.Id));
        Assert.True(await test.Db.Organisations.AnyAsync(x => x.Id == fiji.Id));
    }

    private static async Task<List<string>> ForeignKeyViolationsAsync(ApplicationDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA foreign_key_check;";
            var violations = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) violations.Add(reader.GetString(0));
            return violations;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
