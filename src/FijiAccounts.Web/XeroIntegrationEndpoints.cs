using System.Security.Claims;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web;

public static class XeroIntegrationEndpoints
{
    public static IEndpointRouteBuilder MapXeroIntegrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/o/{organisationId:guid}/integrations/xero/connect", BeginConnection)
            .RequireAuthorization();
        endpoints.MapGet("/api/integrations/xero/callback", CompleteConnection)
            .RequireAuthorization();
        endpoints.MapGet("/api/o/{organisationId:guid}/xero-migration/snapshots/{snapshotId:guid}", DownloadSnapshot)
            .RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> BeginConnection(
        Guid organisationId,
        XeroConnectionService service,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Results.Unauthorized();
        try
        {
            return Results.Redirect(await service.CreateAuthorizationUriAsync(userId, organisationId, cancellationToken));
        }
        catch (UnauthorizedAccessException) { return Results.NotFound(); }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> CompleteConnection(
        string? code,
        string? state,
        string? error,
        XeroConnectionService service,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(error))
            return Results.Problem("The Xero connection was cancelled or declined.", statusCode: StatusCodes.Status400BadRequest);
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return Results.Problem("Xero did not return a complete connection response.", statusCode: StatusCodes.Status400BadRequest);
        try
        {
            var organisationId = await service.CompleteAuthorizationAsync(userId, code, state, cancellationToken);
            return Results.Redirect($"/o/{organisationId}/migration/xero?xero=connected");
        }
        catch (UnauthorizedAccessException) { return Results.NotFound(); }
        catch (Exception ex) when (ex is InvalidOperationException or XeroApiException)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> DownloadSnapshot(
        Guid organisationId,
        Guid snapshotId,
        XeroConnectionService service,
        ClaimsPrincipal principal,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Results.Unauthorized();
        try
        {
            var snapshot = await service.ReadSnapshotAsync(userId, organisationId, snapshotId, cancellationToken);
            if (snapshot is null) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, no-store";
            http.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(snapshot.Content, "application/json", snapshot.FileName);
        }
        catch (UnauthorizedAccessException) { return Results.NotFound(); }
    }
}
