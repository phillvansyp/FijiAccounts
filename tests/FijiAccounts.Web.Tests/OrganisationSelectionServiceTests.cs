using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class OrganisationSelectionServiceTests
{
    [Fact]
    public async Task ExplicitSelectionPersistsAcrossFreshDatabaseContexts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options;
        const string userId = "accountant";
        var jcrId = Guid.NewGuid();

        await using (var db = new ApplicationDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new ApplicationUser { Id = userId, UserName = userId });
            db.Organisations.Add(new Organisation { Id = jcrId, LegalName = "JCR 2006 Limited", Kind = OrganisationKind.Business });
            db.OrganisationMemberships.Add(new OrganisationMembership { OrganisationId = jcrId, UserId = userId, Role = OrganisationRole.Owner });
            await db.SaveChangesAsync();
            var selection = Selection(db);
            Assert.Null(await selection.GetAsync(userId));
            await selection.SelectAsync(userId, jcrId);
        }

        await using (var db = new ApplicationDbContext(options))
        {
            var selection = Selection(db);
            Assert.Equal(jcrId, await selection.GetAsync(userId));
            await selection.SelectAsync(userId, jcrId);
            Assert.Equal(jcrId, await selection.GetAsync(userId));
        }
    }

    [Fact]
    public async Task SelectionRejectsACompanyTheUserCannotAccess()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options;
        const string userId = "owner";
        var permittedId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new ApplicationUser { Id = userId, UserName = userId });
        db.Organisations.AddRange(
            new Organisation { Id = permittedId, LegalName = "Permitted Limited", Kind = OrganisationKind.Business },
            new Organisation { Id = otherCompanyId, LegalName = "Other Limited", Kind = OrganisationKind.Business });
        db.OrganisationMemberships.Add(new OrganisationMembership { OrganisationId = permittedId, UserId = userId, Role = OrganisationRole.Owner });
        await db.SaveChangesAsync();
        var selection = Selection(db);

        await selection.SelectAsync(userId, permittedId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => selection.SelectAsync(userId, otherCompanyId));

        Assert.Equal(permittedId, await selection.GetAsync(userId));
    }

    private static OrganisationSelectionService Selection(ApplicationDbContext db) =>
        new(db, new TenantAccessService(db));
}
