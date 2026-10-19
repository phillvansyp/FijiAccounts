using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using FijiAccounts.Web.Services;
using Microsoft.Extensions.Options;

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
        endpoints.MapPost("/api/integrations/xero/webhook", ReceiveWebhook)
            .AllowAnonymous();
        return endpoints;
    }

    private static async Task<IResult> ReceiveWebhook(
        HttpRequest request,
        XeroReadOnlySyncService sync,
        IOptions<XeroApiOptions> configured,
        CancellationToken cancellationToken)
    {
        const int maximumBytes = 1024 * 1024;
        if (string.IsNullOrWhiteSpace(configured.Value.WebhookKey))
            return Results.Problem("The Xero webhook key is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
        if (request.ContentLength is > maximumBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > maximumBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        var body = buffer.ToArray();
        if (!request.Headers.TryGetValue("x-xero-signature", out var supplied) ||
            !VerifySignature(body, supplied.ToString(), configured.Value.WebhookKey))
            return Results.Unauthorized();

        try
        {
            using var document = JsonDocument.Parse(body);
            var tenantIds = new HashSet<Guid>();
            if (document.RootElement.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in events.EnumerateArray())
                    if (item.TryGetProperty("tenantId", out var tenant) && Guid.TryParse(tenant.GetString(), out var tenantId))
                        tenantIds.Add(tenantId);
            }
            await sync.RequestSyncForTenantsAsync(tenantIds, cancellationToken);
            return Results.Ok();
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }
    }

    private static bool VerifySignature(ReadOnlySpan<byte> body, string supplied, string key)
    {
        try
        {
            var expected = HMACSHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key), body);
            var actual = Convert.FromBase64String(supplied);
            return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
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
            return Results.Redirect($"/o/{organisationId}/integrations/xero?xero=connected");
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
