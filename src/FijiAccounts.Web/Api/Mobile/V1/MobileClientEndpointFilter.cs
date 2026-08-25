using System.Security.Claims;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FijiAccounts.Web.Api.Mobile.V1;

public sealed record MobileClientRequest(
    string Platform,
    Version Version,
    Guid InstallationId);

public sealed record MobileClientValidation(
    MobileClientRequest? Client,
    int? StatusCode = null,
    string? Code = null,
    string? Title = null,
    string? MinimumVersion = null);

public sealed class AllowUnregisteredMobileDevice;

public sealed class MobileClientEndpointFilter(
    IOptions<MobileApiOptions> options,
    ApplicationDbContext db) : IEndpointFilter
{
    public const string PlatformHeader = "X-Client-Platform";
    public const string VersionHeader = "X-Client-Version";
    public const string DeviceHeader = "X-Device-Id";
    public const string ContextItemKey = "MobileClientRequest";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var validation = Validate(
            context.HttpContext.Request.Headers[PlatformHeader].ToString(),
            context.HttpContext.Request.Headers[VersionHeader].ToString(),
            context.HttpContext.Request.Headers[DeviceHeader].ToString(),
            options.Value);
        if (validation.Client is null)
        {
            return Problem(validation);
        }

        context.HttpContext.Items[ContextItemKey] = validation.Client;
        var allowsUnregistered = context.HttpContext.GetEndpoint()?.Metadata
            .GetMetadata<AllowUnregisteredMobileDevice>() is not null;
        if (!allowsUnregistered)
        {
            var userId = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var session = await db.MobileDeviceSessions.SingleOrDefaultAsync(device =>
                device.UserId == userId &&
                device.InstallationId == validation.Client.InstallationId,
                context.HttpContext.RequestAborted);
            if (session is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Device is not registered",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "device_not_registered"
                    });
            }

            if (session.RevokedAt is not null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Device session has been revoked",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "device_session_revoked"
                    });
            }

            if (session.LastSeenAt < DateTimeOffset.UtcNow.AddMinutes(-15))
            {
                session.LastSeenAt = DateTimeOffset.UtcNow;
                session.AppVersion = validation.Client.Version.ToString();
                await db.SaveChangesAsync(context.HttpContext.RequestAborted);
            }
        }

        return await next(context);
    }

    public static MobileClientValidation Validate(
        string? platform,
        string? version,
        string? deviceId,
        MobileApiOptions options)
    {
        var normalizedPlatform = platform?.Trim().ToLowerInvariant();
        if (normalizedPlatform is not ("ios" or "android"))
        {
            return new(null, StatusCodes.Status400BadRequest,
                "invalid_client_platform", "Client platform must be ios or android");
        }

        if (!Version.TryParse(version, out var parsedVersion))
        {
            return new(null, StatusCodes.Status400BadRequest,
                "invalid_client_version", "Client version is missing or invalid");
        }

        if (!Guid.TryParse(deviceId, out var installationId) || installationId == Guid.Empty)
        {
            return new(null, StatusCodes.Status400BadRequest,
                "invalid_device_id", "Device ID is missing or invalid");
        }

        var minimumText = normalizedPlatform == "ios"
            ? options.MinimumIosVersion
            : options.MinimumAndroidVersion;
        if (!Version.TryParse(minimumText, out var minimumVersion))
        {
            throw new InvalidOperationException(
                $"Configured minimum {normalizedPlatform} version is invalid.");
        }

        if (parsedVersion < minimumVersion)
        {
            return new(null, StatusCodes.Status426UpgradeRequired,
                "client_upgrade_required", "A newer application version is required",
                minimumVersion.ToString());
        }

        return new(new MobileClientRequest(
            normalizedPlatform,
            parsedVersion,
            installationId));
    }

    public static MobileClientRequest GetClient(HttpContext context) =>
        (MobileClientRequest)context.Items[ContextItemKey]!;

    private static IResult Problem(MobileClientValidation validation)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["code"] = validation.Code
        };
        if (validation.MinimumVersion is not null)
        {
            extensions["minimumVersion"] = validation.MinimumVersion;
        }

        return Results.Problem(
            statusCode: validation.StatusCode,
            title: validation.Title,
            extensions: extensions);
    }
}
