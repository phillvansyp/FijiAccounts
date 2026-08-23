using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class PlatformAdministrationServiceTests
{
    [Fact]
    public async Task OverviewRequiresPlatformRoleAndStatusChangeIsAuditedAndEnforced()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var administrator = User("platform-admin", "admin@example.com");
        var tenantUser = User("tenant-user", "owner@example.com");
        var role = new IdentityRole(PlatformAdminAccessService.RoleName)
        {
            Id = "platform-role",
            NormalizedName = PlatformAdminAccessService.RoleName.ToUpperInvariant()
        };
        var group = new OrganisationGroup { Name = "North Pacific Group" };
        var company = new Organisation
        {
            OrganisationGroup = group,
            LegalName = "North Pacific Limited",
            CountryCode = "FJ",
            BaseCurrency = "FJD",
            TaxLabel = "VAT",
            Kind = OrganisationKind.Business
        };
        db.Users.AddRange(administrator, tenantUser);
        db.Roles.Add(role);
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = administrator.Id, RoleId = role.Id });
        db.OrganisationGroups.Add(group);
        db.Organisations.Add(company);
        db.OrganisationMemberships.Add(new OrganisationMembership
        {
            Organisation = company,
            UserId = tenantUser.Id,
            Role = OrganisationRole.Owner
        });
        await db.SaveChangesAsync();

        var access = new PlatformAdminAccessService(db);
        var service = new PlatformAdministrationService(db, access);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetOverviewAsync(tenantUser.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SetTenantStatusAsync(tenantUser.Id, group.Id, TenantStatus.Suspended, "Not authorized"));

        var overview = await service.GetOverviewAsync(administrator.Id);
        Assert.Equal(1, overview.TenantCount);
        Assert.Single(overview.Tenants);
        Assert.Equal(1, overview.Tenants[0].CompanyCount);
        Assert.Equal(1, overview.Tenants[0].UserCount);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetTenantStatusAsync(administrator.Id, group.Id, (TenantStatus)999, "Invalid status"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetTenantStatusAsync(administrator.Id, group.Id, TenantStatus.Suspended, new string('x', 501)));
        Assert.Equal(TenantStatus.Active, (await db.OrganisationGroups.SingleAsync()).Status);
        Assert.Empty(await db.PlatformAuditEvents.ToListAsync());

        await service.SetTenantStatusAsync(administrator.Id, group.Id, TenantStatus.Suspended, "Account review");
        Assert.Equal(TenantStatus.Suspended, (await db.OrganisationGroups.SingleAsync()).Status);
        var audit = await db.PlatformAuditEvents.SingleAsync();
        Assert.Equal(administrator.Id, audit.AdministratorUserId);
        Assert.Equal("Account review", audit.Reason);

        var tenantDetails = await service.GetTenantAsync(administrator.Id, group.Id);
        Assert.NotNull(tenantDetails);
        Assert.Single(tenantDetails.RecentPlatformEvents);
        Assert.Equal("TenantStatusChanged", tenantDetails.RecentPlatformEvents[0].EventType);

        var tenantAccess = new TenantAccessService(db);
        Assert.Empty(await tenantAccess.ListAsync(tenantUser.Id));

        audit.Reason = "Tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.PlatformAuditEvents.Remove(await db.PlatformAuditEvents.SingleAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    private static ApplicationUser User(string id, string email) => new()
    {
        Id = id,
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        EmailConfirmed = true
    };
}
