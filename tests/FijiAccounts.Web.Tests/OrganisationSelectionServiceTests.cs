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
            await db.SaveChangesAsync();
            var selection = new OrganisationSelectionService(db);
            Assert.Null(await selection.GetAsync(userId));
            await selection.SelectAsync(userId, jcrId);
        }

        await using (var db = new ApplicationDbContext(options))
        {
            var selection = new OrganisationSelectionService(db);
            Assert.Equal(jcrId, await selection.GetAsync(userId));
            await selection.SelectAsync(userId, jcrId);
            Assert.Equal(jcrId, await selection.GetAsync(userId));
        }
    }
}
