using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace FijiAccounts.Web.Authentication;

public static class MobileAuthenticationSeeder
{
    public static async Task SeedAsync(WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>(
                $"{MobileAuthenticationOptions.SectionName}:Enabled"))
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider
            .GetRequiredService<IOpenIddictApplicationManager>();
        var options = scope.ServiceProvider
            .GetRequiredService<IOptions<MobileAuthenticationOptions>>().Value;
        var existing = await manager.FindByClientIdAsync(options.ClientId);

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = options.ClientId,
            ClientType = OpenIddictConstants.ClientTypes.Public,
            ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
            DisplayName = "Account Island mobile",
            RedirectUris =
            {
                new Uri(options.IosRedirectUri),
                new Uri(options.AndroidRedirectUri)
            },
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.Endpoints.Revocation,
                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                OpenIddictConstants.Permissions.ResponseTypes.Code,
                OpenIddictConstants.Permissions.Scopes.Email,
                OpenIddictConstants.Permissions.Scopes.Profile,
                OpenIddictConstants.Permissions.Prefixes.Scope +
                    MobileAuthenticationExtensions.ApiScope
            },
            Requirements =
            {
                OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange
            }
        };

        if (existing is null)
        {
            await manager.CreateAsync(descriptor);
        }
        else
        {
            await manager.UpdateAsync(existing, descriptor);
        }
    }
}
