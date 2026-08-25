using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

namespace FijiAccounts.Web.Tests;

public sealed class DemoDataServiceTests
{
    [Fact]
    public async Task ResetAndGenerateRejectsNonDevelopmentEnvironment()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var service = new DemoDataService(
            db,
            new DevelopmentEnvironment { EnvironmentName = "Production" },
            new PlatformAdminAccessService(db));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResetAndGenerateAsync("administrator", new DateOnly(2026, 8, 23)));

        Assert.Contains("Development", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResetAndGenerateRejectsUserWithoutPlatformRole()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var user = User("ordinary-user", "ordinary@example.com");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var service = new DemoDataService(
            db,
            new DevelopmentEnvironment(),
            new PlatformAdminAccessService(db));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ResetAndGenerateAsync(user.Id, new DateOnly(2026, 8, 23)));
    }

    [Fact]
    public async Task MissingDedicatedDemoTenantDoesNotModifyProductionData()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var administrator = User("platform-administrator", "admin@example.com");
        var productionCompany = new Organisation
        {
            LegalName = "Production Company",
            CountryCode = "FJ",
            BaseCurrency = "FJD",
            TaxLabel = "VAT",
            Kind = OrganisationKind.Business
        };
        var platformRole = new IdentityRole(PlatformAdminAccessService.RoleName)
        {
            Id = "platform-admin-role",
            NormalizedName = PlatformAdminAccessService.RoleName.ToUpperInvariant()
        };
        db.Users.Add(administrator);
        db.Roles.Add(platformRole);
        db.UserRoles.Add(new IdentityUserRole<string>
        {
            UserId = administrator.Id,
            RoleId = platformRole.Id
        });
        db.Organisations.Add(productionCompany);
        await db.SaveChangesAsync();
        var service = new DemoDataService(
            db,
            new DevelopmentEnvironment(),
            new PlatformAdminAccessService(db));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResetAndGenerateAsync(administrator.Id, new DateOnly(2026, 8, 23)));

        Assert.Contains("Demo tenant does not exist", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(await db.Organisations.AnyAsync(x => x.Id == productionCompany.Id));
    }

    [Fact]
    public async Task ResetAndGenerateCreatesRepeatableBalancedIsolatedDemo()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var administrator = new ApplicationUser
        {
            Id = "platform-administrator",
            UserName = "admin@example.com",
            NormalizedUserName = "ADMIN@EXAMPLE.COM",
            Email = "admin@example.com",
            NormalizedEmail = "ADMIN@EXAMPLE.COM",
            EmailConfirmed = true
        };
        var demoOwner = new ApplicationUser
        {
            Id = "demo-owner",
            UserName = "demo@accountisland.com",
            NormalizedUserName = "DEMO@ACCOUNTISLAND.COM",
            Email = "demo@accountisland.com",
            NormalizedEmail = "DEMO@ACCOUNTISLAND.COM",
            EmailConfirmed = true
        };
        var demoGroup = new OrganisationGroup
        {
            Name = DemoDataService.DemoGroupName,
            PresentationCurrency = "FJD",
            IsDemo = true
        };
        var demoCompany = new Organisation
        {
            OrganisationGroupId = demoGroup.Id,
            LegalName = "Demo",
            CountryCode = "FJ",
            BaseCurrency = "FJD",
            TaxLabel = "VAT",
            Kind = OrganisationKind.Business
        };
        var legacyDemoGroup = new OrganisationGroup
        {
            Id = Guid.Parse("8d13b614-47f4-50eb-a994-7e0ca5c49cc0"),
            Name = "Account Island Demo Group",
            PresentationCurrency = "FJD",
            IsDemo = true
        };
        var legacyDemoCompany = new Organisation
        {
            OrganisationGroupId = legacyDemoGroup.Id,
            LegalName = "Legacy Demo Company",
            CountryCode = "FJ",
            BaseCurrency = "FJD",
            TaxLabel = "VAT",
            Kind = OrganisationKind.Business
        };
        var unrelated = new Organisation
        {
            LegalName = "Unrelated Production Company",
            CountryCode = "FJ",
            BaseCurrency = "FJD",
            TaxLabel = "VAT",
            Kind = OrganisationKind.Business
        };
        db.Users.AddRange(administrator, demoOwner);
        db.OrganisationGroups.AddRange(demoGroup, legacyDemoGroup);
        db.Organisations.AddRange(demoCompany, legacyDemoCompany, unrelated);
        db.OrganisationGroupMemberships.Add(new OrganisationGroupMembership
        {
            OrganisationGroupId = demoGroup.Id,
            UserId = demoOwner.Id,
            Role = OrganisationGroupRole.Owner
        });
        db.OrganisationMemberships.Add(new OrganisationMembership
        {
            OrganisationId = demoCompany.Id,
            UserId = demoOwner.Id,
            Role = OrganisationRole.Owner
        });
        var platformRole = new IdentityRole(PlatformAdminAccessService.RoleName)
        {
            Id = "platform-admin-role",
            NormalizedName = PlatformAdminAccessService.RoleName.ToUpperInvariant()
        };
        db.Roles.Add(platformRole);
        db.UserRoles.Add(new IdentityUserRole<string>
        {
            UserId = administrator.Id,
            RoleId = platformRole.Id
        });
        await db.SaveChangesAsync();

        var service = new DemoDataService(
            db,
            new DevelopmentEnvironment(),
            new PlatformAdminAccessService(db));
        var asOf = new DateOnly(2026, 8, 23);
        var first = await service.ResetAndGenerateAsync(administrator.Id, asOf);

        Assert.Equal(asOf, first.AsOfDate);
        Assert.Equal(asOf.AddMonths(-3).AddDays(1), first.StartDate);
        Assert.Equal(2, first.CompanyCount);
        Assert.Equal(4, first.BranchCount);
        Assert.Equal(9, first.DivisionCount);
        Assert.Equal(40, first.CustomerCount);
        Assert.Equal(24, first.SupplierCount);
        Assert.Equal(180, first.SalesInvoiceCount);
        Assert.Equal(84, first.SupplierBillCount);
        Assert.True(first.CustomerReceiptCount > 0);
        Assert.True(first.SupplierPaymentCount > 0);
        Assert.True(first.CreditNoteCount > 0);
        Assert.InRange(first.AnnualisedNetSales, 4_800_000m, 5_100_000m);

        var demoOrganisationIds = await db.Organisations
            .Where(x => x.OrganisationGroupId == demoGroup.Id)
            .Select(x => x.Id)
            .ToArrayAsync();
        Assert.Contains(demoCompany.Id, demoOrganisationIds);
        Assert.Equal(
            2,
            await db.OrganisationMemberships.CountAsync(x =>
                demoOrganisationIds.Contains(x.OrganisationId) &&
                x.UserId == demoOwner.Id &&
                x.Role == OrganisationRole.Owner));
        Assert.False(await db.OrganisationMemberships.AnyAsync(x =>
            demoOrganisationIds.Contains(x.OrganisationId) &&
            x.UserId == administrator.Id));
        var journals = await db.PostedJournals
            .Where(x => demoOrganisationIds.Contains(x.OrganisationId))
            .Include(x => x.Lines)
            .ToListAsync();
        Assert.NotEmpty(journals);
        Assert.All(journals, journal =>
            Assert.Equal(journal.Lines.Sum(x => x.Debit), journal.Lines.Sum(x => x.Credit)));

        var invoiceDates = await db.SalesInvoices
            .Where(x => demoOrganisationIds.Contains(x.OrganisationId))
            .Select(x => x.IssueDate)
            .ToListAsync();
        Assert.All(invoiceDates, date =>
            Assert.InRange(date, first.StartDate, first.AsOfDate));

        var second = await service.ResetAndGenerateAsync(administrator.Id, asOf);
        Assert.Equal(first, second);
        Assert.Equal(
            2,
            await db.PlatformAuditEvents.CountAsync(x =>
                x.OrganisationGroupId == demoGroup.Id &&
                x.EventType == "DemoDataReset"));
        Assert.True(await db.Organisations.AnyAsync(x => x.Id == unrelated.Id));
        Assert.Equal(1, await db.OrganisationGroups.CountAsync(x => x.Id == demoGroup.Id));
        Assert.False(await db.OrganisationGroups.AnyAsync(x => x.Id == legacyDemoGroup.Id));
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

    private sealed class DevelopmentEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "FijiAccounts.Web.Tests";
        public string WebRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
