using System.Security.Claims;
using FijiAccounts.Web.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace FijiAccounts.Web.Api.Mobile.V1;

public static class MobileApiV1Endpoints
{
    public const string RoutePrefix = "/api/mobile/v1";
    public const string RateLimitPolicy = "mobile-api";
    private const int DefaultPageSize = 25;
    private const int MaximumPageSize = 100;

    public static IEndpointRouteBuilder MapMobileApiV1(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup(RoutePrefix)
            .RequireAuthorization(MobileAuthenticationExtensions.AuthorizationPolicy)
            .RequireRateLimiting(RateLimitPolicy)
            .AddEndpointFilter<MobileClientEndpointFilter>()
            .WithTags("Mobile API v1");

        group.MapGet("/session", GetSession)
            .WithName("MobileV1GetSession")
            .WithSummary("Returns the authenticated mobile API session.")
            .WithMetadata(new AllowUnregisteredMobileDevice());

        group.MapPut("/devices/current", RegisterCurrentDevice)
            .WithName("MobileV1RegisterCurrentDevice")
            .WithSummary("Registers or refreshes the current device installation.")
            .WithMetadata(new AllowUnregisteredMobileDevice());

        group.MapGet("/devices", ListDevices)
            .WithName("MobileV1ListDevices")
            .WithSummary("Lists device sessions belonging to the authenticated user.");

        group.MapDelete("/devices/{deviceSessionId:guid}", RevokeDevice)
            .WithName("MobileV1RevokeDevice")
            .WithSummary("Revokes one of the authenticated user's device sessions.");

        group.MapGet("/organisations", ListOrganisations)
            .WithName("MobileV1ListOrganisations")
            .WithSummary("Lists organisations available to the authenticated user.");

        group.MapGet("/organisations/{organisationId:guid}/capabilities", GetCapabilities)
            .WithName("MobileV1GetOrganisationCapabilities")
            .WithSummary("Returns effective organisation and dimension capabilities.");

        group.MapGet("/organisations/{organisationId:guid}/dashboard", GetDashboard)
            .WithName("MobileV1GetOrganisationDashboard")
            .WithSummary("Returns the organisation dashboard within the user's dimension scope.");

        group.MapGet("/organisations/{organisationId:guid}/notifications", ListNotifications)
            .WithName("MobileV1ListOrganisationNotifications")
            .WithSummary("Lists unread organisation notifications.");

        group.MapPost(
                "/organisations/{organisationId:guid}/notifications/{notificationId:guid}/read",
                MarkNotificationRead)
            .WithName("MobileV1MarkOrganisationNotificationRead")
            .WithSummary("Marks an organisation notification as read.");

        return endpoints;
    }

    private static async Task<IResult> GetSession(
        ClaimsPrincipal user,
        HttpContext context,
        MobileDeviceSessionService devices,
        IOptions<MobileApiOptions> options,
        CancellationToken cancellationToken)
    {
        var userId = UserId(user);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var client = MobileClientEndpointFilter.GetClient(context);
        var device = await devices.GetStateAsync(
            userId,
            client.InstallationId,
            cancellationToken);
        return Results.Ok(new MobileSessionResponse(
            userId,
            user.Identity?.Name,
            "v1",
            options.Value.MinimumIosVersion,
            options.Value.MinimumAndroidVersion,
            device.Registered,
            device.Revoked));
    }

    private static async Task<IResult> RegisterCurrentDevice(
        MobileDeviceRegistrationRequest? registration,
        ClaimsPrincipal user,
        HttpContext context,
        MobileDeviceSessionService devices,
        CancellationToken cancellationToken)
    {
        var userId = UserId(user);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var result = await devices.RegisterAsync(
                userId,
                MobileClientEndpointFilter.GetClient(context),
                registration?.DisplayName,
                user.GetAuthorizationId(),
                cancellationToken);
            return result.Status == MobileDeviceRegistrationStatus.Revoked
                ? Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Device session has been revoked",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "device_session_revoked"
                    })
                : Results.Ok(result.Device);
        }
        catch (ArgumentException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: exception.Message,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "invalid_device_name"
                });
        }
    }

    private static async Task<IResult> ListDevices(
        ClaimsPrincipal user,
        HttpContext context,
        MobileDeviceSessionService devices,
        CancellationToken cancellationToken)
    {
        var userId = UserId(user);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var client = MobileClientEndpointFilter.GetClient(context);
        return Results.Ok(await devices.ListAsync(
            userId,
            client.InstallationId,
            cancellationToken));
    }

    private static async Task<IResult> RevokeDevice(
        Guid deviceSessionId,
        ClaimsPrincipal user,
        MobileDeviceSessionService devices,
        CancellationToken cancellationToken)
    {
        var userId = UserId(user);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        return await devices.RevokeAsync(userId, deviceSessionId, cancellationToken)
            ? Results.NoContent()
            : Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Device session not found",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "device_session_not_found"
                });
    }

    private static async Task<IResult> ListOrganisations(
        ClaimsPrincipal user,
        MobileApiV1Service mobileApi)
    {
        var userId = UserId(user);
        return userId is null
            ? Results.Unauthorized()
            : Results.Ok(await mobileApi.ListOrganisationsAsync(userId));
    }

    private static async Task<IResult> GetCapabilities(
        Guid organisationId,
        ClaimsPrincipal user,
        MobileApiV1Service mobileApi,
        CancellationToken cancellationToken)
    {
        var userId = UserId(user);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var capabilities = await mobileApi.GetCapabilitiesAsync(
            userId,
            organisationId,
            cancellationToken);
        return capabilities is null
            ? Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Organisation not found",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "organisation_not_found"
                })
            : Results.Ok(capabilities);
    }

    private static async Task<IResult> GetDashboard(
        Guid organisationId,
        ClaimsPrincipal user,
        MobileApiV1Service mobileApi,
        CancellationToken cancellationToken)
    {
        var userId = UserId(user);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var dashboard = await mobileApi.GetDashboardAsync(
            userId,
            organisationId,
            cancellationToken);
        return dashboard is null
            ? OrganisationNotFound()
            : Results.Ok(dashboard);
    }

    private static async Task<IResult> ListNotifications(
        Guid organisationId,
        string? cursor,
        int? limit,
        ClaimsPrincipal user,
        MobileApiV1Service mobileApi,
        CancellationToken cancellationToken)
    {
        var userId = UserId(user);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var pageSize = limit ?? DefaultPageSize;
        if (pageSize is < 1 or > MaximumPageSize)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid page size",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "invalid_page_size"
                });
        }

        if (!MobileApiCursor.TryDecode(cursor, out var beforeCreatedAtTicks, out var beforeId))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid cursor",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "invalid_cursor"
                });
        }

        var notifications = await mobileApi.ListNotificationsAsync(
            userId,
            organisationId,
            beforeCreatedAtTicks,
            beforeId,
            pageSize,
            cancellationToken);
        return notifications is null
            ? OrganisationNotFound()
            : Results.Ok(notifications);
    }

    private static async Task<IResult> MarkNotificationRead(
        Guid organisationId,
        Guid notificationId,
        ClaimsPrincipal user,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        HttpResponse response,
        MobileApiV1Service mobileApi,
        CancellationToken cancellationToken)
    {
        var userId = UserId(user);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        if (!MobileIdempotencyService.IsValidKey(idempotencyKey))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "A valid Idempotency-Key header is required",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "idempotency_key_required"
                });
        }

        var result = await mobileApi.MarkNotificationReadAsync(
            userId,
            organisationId,
            notificationId,
            idempotencyKey!,
            cancellationToken);
        if (result is null)
        {
            return OrganisationNotFound();
        }

        if (result.Replayed)
        {
            response.Headers["Idempotency-Replayed"] = "true";
        }

        return result.StatusCode switch
        {
            StatusCodes.Status204NoContent => Results.NoContent(),
            StatusCodes.Status404NotFound => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Notification not found",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "notification_not_found"
                }),
            StatusCodes.Status409Conflict => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Idempotency key already used",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "idempotency_key_reused"
                }),
            _ => Results.StatusCode(result.StatusCode)
        };
    }

    private static IResult OrganisationNotFound() =>
        Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Organisation not found",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "organisation_not_found"
            });

    private static string? UserId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier);
}
