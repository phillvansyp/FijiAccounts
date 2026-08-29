using System.Security.Cryptography.X509Certificates;
using FijiAccounts.Web.Data;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Validation.AspNetCore;

namespace FijiAccounts.Web.Authentication;

public static class MobileAuthenticationExtensions
{
    public const string ApiScope = "fiji_accounts_api";
    public const string ApiResource = "fiji_accounts_api";
    public const string AuthorizationPolicy = "mobile-api-access";
    public const string DeviceIdClaim = "account_island_device_id";

    public static void AddMobileAuthentication(
        this WebApplicationBuilder builder,
        bool enabled)
    {
        builder.Services.AddOptions<MobileAuthenticationOptions>()
            .Bind(builder.Configuration.GetSection(MobileAuthenticationOptions.SectionName))
            .Validate(
                options => !options.Enabled || !string.IsNullOrWhiteSpace(options.ClientId),
                "MobileAuthentication:ClientId is required when mobile authentication is enabled.")
            .Validate(
                options => !options.Enabled || IsHttpsUri(options.IosRedirectUri),
                "MobileAuthentication:IosRedirectUri must be an absolute HTTPS URI.")
            .Validate(
                options => !options.Enabled || IsHttpsUri(options.AndroidRedirectUri),
                "MobileAuthentication:AndroidRedirectUri must be an absolute HTTPS URI.")
            .ValidateOnStart();

        if (!enabled)
        {
            builder.Services.AddAuthorizationBuilder()
                .AddPolicy(AuthorizationPolicy, policy =>
                    policy.AddAuthenticationSchemes(IdentityConstants.ApplicationScheme)
                        .RequireAuthenticatedUser());
            return;
        }

        builder.Services.AddOpenIddict()
            .AddCore(options => options.UseEntityFrameworkCore()
                .UseDbContext<ApplicationDbContext>())
            .AddServer(options =>
            {
                options.SetAuthorizationEndpointUris("/connect/authorize")
                    .SetTokenEndpointUris("/connect/token")
                    .SetRevocationEndpointUris("/connect/revoke")
                    .AllowAuthorizationCodeFlow()
                    .AllowRefreshTokenFlow()
                    .RequireProofKeyForCodeExchange()
                    .RegisterScopes(
                        OpenIddictConstants.Scopes.Email,
                        OpenIddictConstants.Scopes.OpenId,
                        OpenIddictConstants.Scopes.Profile,
                        OpenIddictConstants.Scopes.OfflineAccess,
                        ApiScope)
                    .SetAccessTokenLifetime(TimeSpan.FromMinutes(15))
                    .SetRefreshTokenLifetime(TimeSpan.FromDays(30));

                AddCredentials(options, builder);

                options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough();
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.EnableTokenEntryValidation();
                options.UseAspNetCore();
            });
        builder.Services.PostConfigure<OpenIddictServerOptions>(options =>
        {
            options.CodeChallengeMethods.Clear();
            options.CodeChallengeMethods.Add(
                OpenIddictConstants.CodeChallengeMethods.Sha256);
        });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicy, policy =>
            {
                policy.AddAuthenticationSchemes(
                        IdentityConstants.ApplicationScheme,
                        OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
                    .RequireAuthenticatedUser()
                    .RequireAssertion(context =>
                        context.User.Identity?.AuthenticationType ==
                            IdentityConstants.ApplicationScheme ||
                        context.User.HasScope(ApiScope));
            });
    }

    private static void AddCredentials(
        OpenIddictServerBuilder options,
        WebApplicationBuilder builder)
    {
        if (builder.Environment.IsEnvironment("Testing"))
        {
            options.AddEphemeralEncryptionKey()
                .AddEphemeralSigningKey();
            return;
        }

        if (builder.Environment.IsDevelopment())
        {
            options.AddDevelopmentEncryptionCertificate()
                .AddDevelopmentSigningCertificate();
            return;
        }

        var configuration = builder.Configuration
            .GetSection(MobileAuthenticationOptions.SectionName)
            .Get<MobileAuthenticationOptions>() ?? new MobileAuthenticationOptions();
        if (string.IsNullOrWhiteSpace(configuration.SigningCertificatePath) ||
            string.IsNullOrWhiteSpace(configuration.EncryptionCertificatePath))
        {
            throw new InvalidOperationException(
                "Mobile authentication requires signing and encryption certificates in production.");
        }

        options.AddSigningCertificate(LoadCertificate(
                configuration.SigningCertificatePath,
                configuration.SigningCertificatePassword))
            .AddEncryptionCertificate(LoadCertificate(
                configuration.EncryptionCertificatePath,
                configuration.EncryptionCertificatePassword));
    }

    private static X509Certificate2 LoadCertificate(string path, string? password) =>
        X509CertificateLoader.LoadPkcs12FromFile(
            path,
            password,
            X509KeyStorageFlags.EphemeralKeySet);

    private static bool IsHttpsUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps;
}
