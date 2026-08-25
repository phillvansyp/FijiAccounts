using System.Security.Claims;
using FijiAccounts.Web.Data;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace FijiAccounts.Web.Authentication;

public static class MobileAuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapMobileAuthenticationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods(
            "/connect/authorize",
            [HttpMethods.Get, HttpMethods.Post],
            AuthorizeAsync);
        endpoints.MapPost("/connect/token", ExchangeAsync)
            .DisableAntiforgery();

        return endpoints;
    }

    private static async Task<IResult> AuthorizeAsync(
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        var request = context.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenID Connect request is unavailable.");
        if (!Guid.TryParse(request.GetParameter("device_id")?.ToString(), out var deviceId) ||
            deviceId == Guid.Empty)
        {
            return Forbid(
                OpenIddictConstants.Errors.InvalidRequest,
                "A valid device_id parameter is required.");
        }

        if (context.User.Identity?.IsAuthenticated is not true)
        {
            return Results.Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = context.Request.PathBase +
                        context.Request.Path + context.Request.QueryString
                },
                [IdentityConstants.ApplicationScheme]);
        }

        var user = await userManager.GetUserAsync(context.User);
        if (user is null || !await signInManager.CanSignInAsync(user))
        {
            return Forbid(
                OpenIddictConstants.Errors.AccessDenied,
                "The account is no longer allowed to sign in.");
        }

        var principal = await CreatePrincipalAsync(
            user,
            request.GetScopes(),
            deviceId,
            signInManager);
        return Results.SignIn(
            principal,
            authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> ExchangeAsync(
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ApplicationDbContext db)
    {
        var request = context.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenID Connect request is unavailable.");
        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
        {
            return Forbid(
                OpenIddictConstants.Errors.UnsupportedGrantType,
                "Only authorization-code and refresh-token exchanges are supported.");
        }

        var authentication = await context.AuthenticateAsync(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var subject = authentication.Principal?.GetClaim(OpenIddictConstants.Claims.Subject);
        var user = subject is null ? null : await userManager.FindByIdAsync(subject);
        if (user is null || !await signInManager.CanSignInAsync(user))
        {
            return Forbid(
                OpenIddictConstants.Errors.InvalidGrant,
                "The token is no longer valid for this account.");
        }

        var securityStamp = authentication.Principal?.FindFirstValue(
            userManager.Options.ClaimsIdentity.SecurityStampClaimType);
        if (userManager.SupportsUserSecurityStamp &&
            securityStamp != await userManager.GetSecurityStampAsync(user))
        {
            return Forbid(
                OpenIddictConstants.Errors.InvalidGrant,
                "The account security state has changed. Sign in again.");
        }

        var deviceIdText = authentication.Principal?.FindFirstValue(
            MobileAuthenticationExtensions.DeviceIdClaim);
        if (!Guid.TryParse(deviceIdText, out var deviceId) || deviceId == Guid.Empty)
        {
            return Forbid(
                OpenIddictConstants.Errors.InvalidGrant,
                "The token is not bound to a valid device.");
        }

        if (request.IsRefreshTokenGrantType())
        {
            var activeDevice = await db.MobileDeviceSessions.AnyAsync(device =>
                device.UserId == user.Id &&
                device.InstallationId == deviceId &&
                device.RevokedAt == null,
                context.RequestAborted);
            if (!activeDevice)
            {
                return Forbid(
                    OpenIddictConstants.Errors.InvalidGrant,
                    "The device session is missing or has been revoked.");
            }
        }

        var scopes = authentication.Principal?.GetScopes() ?? [];
        var principal = await CreatePrincipalAsync(user, scopes, deviceId, signInManager);
        return Results.SignIn(
            principal,
            authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<ClaimsPrincipal> CreatePrincipalAsync(
        ApplicationUser user,
        IEnumerable<string> scopes,
        Guid deviceId,
        SignInManager<ApplicationUser> signInManager)
    {
        var principal = await signInManager.CreateUserPrincipalAsync(user);
        principal.SetClaim(OpenIddictConstants.Claims.Subject, user.Id);
        principal.SetClaim(OpenIddictConstants.Claims.Name, user.UserName);
        principal.SetClaim(OpenIddictConstants.Claims.Email, user.Email);
        principal.SetClaim(
            MobileAuthenticationExtensions.DeviceIdClaim,
            deviceId.ToString());
        principal.SetScopes(scopes);
        principal.SetResources(MobileAuthenticationExtensions.ApiResource);

        foreach (var claim in principal.Claims)
        {
            claim.SetDestinations(GetDestinations(claim, principal));
        }

        return principal;
    }

    private static IEnumerable<string> GetDestinations(
        Claim claim,
        ClaimsPrincipal principal)
    {
        var isSubject = claim.Type is OpenIddictConstants.Claims.Subject ||
            claim.Type == ClaimTypes.NameIdentifier;
        var isName = claim.Type is OpenIddictConstants.Claims.Name ||
            claim.Type == ClaimTypes.Name;
        var isEmail = claim.Type is OpenIddictConstants.Claims.Email ||
            claim.Type == ClaimTypes.Email;
        if (isSubject ||
            isName && principal.HasScope(OpenIddictConstants.Scopes.Profile) ||
            isEmail && principal.HasScope(OpenIddictConstants.Scopes.Email) ||
            claim.Type == ClaimTypes.Role ||
            claim.Type == MobileAuthenticationExtensions.DeviceIdClaim)
        {
            yield return OpenIddictConstants.Destinations.AccessToken;
        }

        if (isSubject ||
            isName && principal.HasScope(OpenIddictConstants.Scopes.Profile) ||
            isEmail && principal.HasScope(OpenIddictConstants.Scopes.Email))
        {
            yield return OpenIddictConstants.Destinations.IdentityToken;
        }
    }

    private static IResult Forbid(string error, string description) =>
        Results.Forbid(
            new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
            }),
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
}
