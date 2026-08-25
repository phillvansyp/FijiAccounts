using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FijiAccounts.Web.Authentication;
using FijiAccounts.Web.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FijiAccounts.Web.Tests;

public sealed class MobileAuthenticationIntegrationTests
{
    [Fact]
    public async Task PkceRefreshAndDeviceRevocationAreEnforcedEndToEnd()
    {
        await using var factory = new MobileAuthenticationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        var cookie = await CreateIdentityCookieAsync(factory.Services);
        client.DefaultRequestHeaders.Add("Cookie", cookie);

        var discoveryResponse = await client.GetAsync("/.well-known/openid-configuration");
        discoveryResponse.EnsureSuccessStatusCode();
        using var discovery = JsonDocument.Parse(
            await discoveryResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            ["S256"],
            discovery.RootElement.GetProperty("code_challenge_methods_supported")
                .EnumerateArray()
                .Select(value => value.GetString()));

        var deviceId = Guid.NewGuid();
        const string verifier =
            "test-verifier-with-at-least-forty-three-characters-123456789";
        var challenge = WebEncoders.Base64UrlEncode(
            SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var authorizeUri = QueryHelpers.AddQueryString(
            "/connect/authorize",
            new Dictionary<string, string?>
            {
                ["client_id"] = MobileAuthenticationFactory.ClientId,
                ["redirect_uri"] = MobileAuthenticationFactory.RedirectUri,
                ["response_type"] = "code",
                ["scope"] = "openid profile email offline_access " +
                    MobileAuthenticationExtensions.ApiScope,
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["device_id"] = deviceId.ToString(),
                ["state"] = "integration-test"
            });
        var authorizeResponse = await client.GetAsync(authorizeUri);

        Assert.Equal(HttpStatusCode.Redirect, authorizeResponse.StatusCode);
        var callback = authorizeResponse.Headers.Location ??
            throw new InvalidOperationException("Authorization did not return a redirect.");
        Assert.Equal(MobileAuthenticationFactory.RedirectUri, callback.GetLeftPart(UriPartial.Path));
        var callbackQuery = QueryHelpers.ParseQuery(callback.Query);
        var code = Assert.Single(callbackQuery["code"]);
        Assert.Equal("integration-test", Assert.Single(callbackQuery["state"]));

        using var token = await ExchangeCodeAsync(client, code!, verifier);
        var accessToken = token.RootElement.GetProperty("access_token").GetString();
        var refreshToken = token.RootElement.GetProperty("refresh_token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        Assert.False(string.IsNullOrWhiteSpace(refreshToken));

        SetMobileHeaders(client, accessToken!, deviceId);
        var sessionResponse = await client.GetAsync("/api/mobile/v1/session");
        sessionResponse.EnsureSuccessStatusCode();
        var registrationResponse = await client.PutAsJsonAsync(
            "/api/mobile/v1/devices/current",
            new { displayName = "Integration test phone" });
        registrationResponse.EnsureSuccessStatusCode();
        using var registration = JsonDocument.Parse(
            await registrationResponse.Content.ReadAsStringAsync());
        var deviceSessionId = registration.RootElement.GetProperty("id").GetGuid();

        using var refreshed = await RefreshAsync(client, refreshToken!);
        var rotatedAccessToken = refreshed.RootElement.GetProperty("access_token").GetString();
        var rotatedRefreshToken = refreshed.RootElement.GetProperty("refresh_token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(rotatedAccessToken));
        Assert.False(string.IsNullOrWhiteSpace(rotatedRefreshToken));
        Assert.NotEqual(refreshToken, rotatedRefreshToken);

        SetMobileHeaders(client, rotatedAccessToken!, deviceId);
        var revokeResponse = await client.DeleteAsync(
            $"/api/mobile/v1/devices/{deviceSessionId}");
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);

        var rejectedRefresh = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = MobileAuthenticationFactory.ClientId,
                ["refresh_token"] = rotatedRefreshToken!
            }));
        Assert.Equal(HttpStatusCode.BadRequest, rejectedRefresh.StatusCode);
        using var error = JsonDocument.Parse(
            await rejectedRefresh.Content.ReadAsStringAsync());
        Assert.Equal(
            "invalid_grant",
            error.RootElement.GetProperty("error").GetString());
    }

    private static async Task<JsonDocument> ExchangeCodeAsync(
        HttpClient client,
        string code,
        string verifier)
    {
        var response = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = MobileAuthenticationFactory.ClientId,
                ["redirect_uri"] = MobileAuthenticationFactory.RedirectUri,
                ["code"] = code,
                ["code_verifier"] = verifier
            }));
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonDocument> RefreshAsync(
        HttpClient client,
        string refreshToken)
    {
        var response = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = MobileAuthenticationFactory.ClientId,
                ["refresh_token"] = refreshToken
            }));
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static void SetMobileHeaders(
        HttpClient client,
        string accessToken,
        Guid deviceId)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Remove("X-Client-Platform");
        client.DefaultRequestHeaders.Remove("X-Client-Version");
        client.DefaultRequestHeaders.Remove("X-Device-Id");
        client.DefaultRequestHeaders.Add("X-Client-Platform", "ios");
        client.DefaultRequestHeaders.Add("X-Client-Version", "1.0.0");
        client.DefaultRequestHeaders.Add("X-Device-Id", deviceId.ToString());
    }

    private static async Task<string> CreateIdentityCookieAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = "mobile-auth@example.com",
            Email = "mobile-auth@example.com",
            EmailConfirmed = true
        };
        var result = await users.CreateAsync(user);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(x => x.Description)));

        var principal = await signIn.CreateUserPrincipalAsync(user);
        var protector = scope.ServiceProvider
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(
                "Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware",
                IdentityConstants.ApplicationScheme,
                "v2");
        var format = new TicketDataFormat(protector);
        var value = format.Protect(new AuthenticationTicket(
            principal,
            IdentityConstants.ApplicationScheme));
        return $".AspNetCore.Identity.Application={value}";
    }

    private sealed class MobileAuthenticationFactory : WebApplicationFactory<Program>
    {
        public const string ClientId = "account-island-integration-tests";
        public const string RedirectUri = "https://client.example/callback/ios";
        private readonly string databasePath = Path.Combine(
            Path.GetTempPath(),
            $"fiji-accounts-mobile-auth-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing")
                .UseSetting(
                    "ConnectionStrings:DefaultConnection",
                    $"Data Source={databasePath};Cache=Shared;Pooling=False")
                .UseSetting("Database:MigrateOnStartup", "true")
                .UseSetting("MobileAuthentication:Enabled", "true")
                .UseSetting("MobileAuthentication:ClientId", ClientId)
                .UseSetting("MobileAuthentication:IosRedirectUri", RedirectUri)
                .UseSetting(
                    "MobileAuthentication:AndroidRedirectUri",
                    "https://client.example/callback/android");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] =
                        $"Data Source={databasePath};Cache=Shared;Pooling=False",
                    ["Database:MigrateOnStartup"] = "true",
                    ["MobileAuthentication:Enabled"] = "true",
                    ["MobileAuthentication:ClientId"] = ClientId,
                    ["MobileAuthentication:IosRedirectUri"] = RedirectUri,
                    ["MobileAuthentication:AndroidRedirectUri"] =
                        "https://client.example/callback/android"
                }));
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            File.Delete(databasePath);
        }
    }
}
