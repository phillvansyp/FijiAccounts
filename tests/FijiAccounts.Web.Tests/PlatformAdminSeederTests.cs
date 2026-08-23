using FijiAccounts.Web.Services;
using Microsoft.Extensions.Configuration;

namespace FijiAccounts.Web.Tests;

public sealed class PlatformAdminSeederTests
{
    [Fact]
    public void Production_DoesNotFallBackToDevelopmentSeedEmail()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["DevSeed:Email"] = "developer@example.com"
        });

        var email = PlatformAdminSeeder.ResolveAdministratorEmail(configuration, false);

        Assert.Null(email);
    }

    [Fact]
    public void ExplicitPlatformAdministratorEmail_IsUsedInEveryEnvironment()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["PlatformAdmin:Email"] = "platform@example.com",
            ["DevSeed:Email"] = "developer@example.com"
        });

        Assert.Equal(
            "platform@example.com",
            PlatformAdminSeeder.ResolveAdministratorEmail(configuration, false));
        Assert.Equal(
            "platform@example.com",
            PlatformAdminSeeder.ResolveAdministratorEmail(configuration, true));
    }

    [Fact]
    public void Development_FallsBackToDevelopmentSeedEmail()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["DevSeed:Email"] = "developer@example.com"
        });

        Assert.Equal(
            "developer@example.com",
            PlatformAdminSeeder.ResolveAdministratorEmail(configuration, true));
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
