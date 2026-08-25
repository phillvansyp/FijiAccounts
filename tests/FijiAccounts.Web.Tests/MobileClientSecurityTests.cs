using FijiAccounts.Web.Api.Mobile.V1;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace FijiAccounts.Web.Tests;

public sealed class MobileClientSecurityTests
{
    private static readonly MobileApiOptions Options = new()
    {
        MinimumIosVersion = "2.1.0",
        MinimumAndroidVersion = "3.0.0"
    };

    [Fact]
    public void ValidClientMetadataIsNormalized()
    {
        var installationId = Guid.NewGuid();

        var result = MobileClientEndpointFilter.Validate(
            " IOS ",
            "2.2.0",
            installationId.ToString(),
            Options);

        Assert.NotNull(result.Client);
        Assert.Equal("ios", result.Client.Platform);
        Assert.Equal(new Version(2, 2, 0), result.Client.Version);
        Assert.Equal(installationId, result.Client.InstallationId);
    }

    [Theory]
    [InlineData("windows", "3.0.0", "invalid_client_platform", 400)]
    [InlineData("ios", "not-a-version", "invalid_client_version", 400)]
    [InlineData("ios", "2.0.9", "client_upgrade_required", 426)]
    [InlineData("android", "2.9.9", "client_upgrade_required", 426)]
    public void InvalidOrObsoleteClientsReturnStableErrors(
        string platform,
        string version,
        string expectedCode,
        int expectedStatus)
    {
        var result = MobileClientEndpointFilter.Validate(
            platform,
            version,
            Guid.NewGuid().ToString(),
            Options);

        Assert.Null(result.Client);
        Assert.Equal(expectedCode, result.Code);
        Assert.Equal(expectedStatus, result.StatusCode);
    }

    [Fact]
    public async Task DeviceRegistrationIsUserScopedAndRevocationIsPermanent()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new MobileDeviceSessionService(test.Db);
        var installationId = Guid.NewGuid();
        var client = new MobileClientRequest(
            "android",
            new Version(3, 4, 0),
            installationId);

        var registered = await service.RegisterAsync(
            test.UserId,
            client,
            "Phill's phone");
        var ownDevices = await service.ListAsync(test.UserId, installationId);
        var outsiderId = Guid.NewGuid().ToString();
        var outsiderDevices = await service.ListAsync(outsiderId, installationId);
        var outsiderRevoke = await service.RevokeAsync(
            outsiderId,
            registered.Device.Id);
        var revoked = await service.RevokeAsync(
            test.UserId,
            registered.Device.Id);
        var registrationAfterRevocation = await service.RegisterAsync(
            test.UserId,
            client,
            "Renamed phone");

        Assert.Equal(MobileDeviceRegistrationStatus.Registered, registered.Status);
        var ownDevice = Assert.Single(ownDevices);
        Assert.True(ownDevice.IsCurrent);
        Assert.Equal("Phill's phone", ownDevice.DisplayName);
        Assert.Empty(outsiderDevices);
        Assert.False(outsiderRevoke);
        Assert.True(revoked);
        Assert.Equal(
            MobileDeviceRegistrationStatus.Revoked,
            registrationAfterRevocation.Status);
        Assert.NotNull(registrationAfterRevocation.Device.RevokedAt);
        Assert.Equal(test.UserId, (await test.Db.MobileDeviceSessions
            .AsNoTracking()
            .SingleAsync()).RevokedByUserId);
    }

    [Fact]
    public async Task EndpointFilterAllowsBootstrapAndBlocksMissingOrRevokedDevices()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var installationId = Guid.NewGuid();
        var client = new MobileClientRequest(
            "ios",
            new Version(2, 1, 0),
            installationId);
        var devices = new MobileDeviceSessionService(test.Db);
        var filter = new MobileClientEndpointFilter(
            Microsoft.Extensions.Options.Options.Create(Options),
            test.Db);

        var bootstrap = CreateFilterContext(test.UserId, client, allowUnregistered: true);
        Assert.Equal("allowed", await filter.InvokeAsync(
            bootstrap,
            _ => ValueTask.FromResult<object?>("allowed")));

        var missing = CreateFilterContext(test.UserId, client);
        var missingResult = Assert.IsAssignableFrom<IResult>(await filter.InvokeAsync(
            missing,
            _ => ValueTask.FromResult<object?>("allowed")));
        await missingResult.ExecuteAsync(missing.HttpContext);
        Assert.Equal(401, missing.HttpContext.Response.StatusCode);
        Assert.Contains(
            "device_not_registered",
            await ReadResponseAsync(missing.HttpContext));

        var registered = await devices.RegisterAsync(test.UserId, client, "Test phone");
        var active = CreateFilterContext(test.UserId, client);
        Assert.Equal("allowed", await filter.InvokeAsync(
            active,
            _ => ValueTask.FromResult<object?>("allowed")));

        await devices.RevokeAsync(test.UserId, registered.Device.Id);
        var revoked = CreateFilterContext(test.UserId, client);
        var revokedResult = Assert.IsAssignableFrom<IResult>(await filter.InvokeAsync(
            revoked,
            _ => ValueTask.FromResult<object?>("allowed")));
        await revokedResult.ExecuteAsync(revoked.HttpContext);
        Assert.Equal(401, revoked.HttpContext.Response.StatusCode);
        Assert.Contains(
            "device_session_revoked",
            await ReadResponseAsync(revoked.HttpContext));
    }

    private static TestEndpointFilterInvocationContext CreateFilterContext(
        string userId,
        MobileClientRequest client,
        bool allowUnregistered = false)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddProblemDetails()
            .BuildServiceProvider();
        var http = new DefaultHttpContext
        {
            RequestServices = services,
            Response = { Body = new MemoryStream() },
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId)],
                "Test"))
        };
        http.Request.Headers[MobileClientEndpointFilter.PlatformHeader] = client.Platform;
        http.Request.Headers[MobileClientEndpointFilter.VersionHeader] = client.Version.ToString();
        http.Request.Headers[MobileClientEndpointFilter.DeviceHeader] = client.InstallationId.ToString();
        http.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            allowUnregistered
                ? new EndpointMetadataCollection(new AllowUnregisteredMobileDevice())
                : new EndpointMetadataCollection(),
            "mobile test"));
        return new TestEndpointFilterInvocationContext(http);
    }

    private static async Task<string> ReadResponseAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return await new StreamReader(context.Response.Body).ReadToEndAsync();
    }

    private sealed class TestEndpointFilterInvocationContext(HttpContext httpContext)
        : EndpointFilterInvocationContext
    {
        public override HttpContext HttpContext { get; } = httpContext;

        public override IList<object?> Arguments { get; } = [];

        public override T GetArgument<T>(int index) =>
            (T)Arguments[index]!;
    }
}
