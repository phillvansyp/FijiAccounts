using FijiAccounts.Web.Services;

namespace FijiAccounts.Web;

public static class NzPayIntegrationEndpoints
{
    private const string Prefix = "/api/integrations/nz-pay/v1/organisations/{organisationId:guid}";

    public static void MapNzPayIntegrationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(Prefix).DisableAntiforgery();
        group.MapGet("/fixed-assets", SearchAssets);
        group.MapGet("/fixed-assets/{assetId:guid}", GetAsset);
        group.MapPost("/fbt-journals", CreateFbtJournal);
    }

    private static async Task<IResult> SearchAssets(
        HttpContext context,
        Guid organisationId,
        string? query,
        NzPayInboundIntegrationService integration,
        CancellationToken ct)
    {
        var credential = await Authorize(context, organisationId,
            NzPayInboundIntegrationService.FixedAssetsReadScope, integration, ct);
        if (credential is null) return Results.Unauthorized();
        try
        {
            return Results.Ok(new { assets = await integration.SearchAssetsAsync(organisationId, query, ct) });
        }
        catch (InvalidOperationException ex) { return Problem(ex.Message, StatusCodes.Status400BadRequest); }
    }

    private static async Task<IResult> GetAsset(
        HttpContext context,
        Guid organisationId,
        Guid assetId,
        NzPayInboundIntegrationService integration,
        CancellationToken ct)
    {
        var credential = await Authorize(context, organisationId,
            NzPayInboundIntegrationService.FixedAssetsReadScope, integration, ct);
        if (credential is null) return Results.Unauthorized();
        try
        {
            var asset = await integration.GetAssetAsync(organisationId, assetId, ct);
            return asset is null ? Results.NotFound() : Results.Ok(new { asset });
        }
        catch (InvalidOperationException ex) { return Problem(ex.Message, StatusCodes.Status400BadRequest); }
    }

    private static async Task<IResult> CreateFbtJournal(
        HttpContext context,
        Guid organisationId,
        NzPayFbtJournalPayload request,
        NzPayInboundIntegrationService integration,
        CancellationToken ct)
    {
        var credential = await Authorize(context, organisationId,
            NzPayInboundIntegrationService.FbtJournalsCreateScope, integration, ct);
        if (credential is null) return Results.Unauthorized();
        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var key) || string.IsNullOrWhiteSpace(key))
            return Problem("An Idempotency-Key header is required.", StatusCodes.Status400BadRequest);
        try
        {
            var receipt = await integration.CreateDraftAsync(credential, key.ToString(), request, ct);
            return Results.Ok(new { journalId = receipt.JournalId.ToString(), receipt.Status, receipt.ReceivedAt });
        }
        catch (NzPayIntegrationConflictException ex) { return Problem(ex.Message, StatusCodes.Status409Conflict); }
        catch (InvalidOperationException ex) { return Problem(ex.Message, StatusCodes.Status400BadRequest); }
    }

    private static async Task<Data.NzPayApiCredential?> Authorize(
        HttpContext context,
        Guid organisationId,
        string scope,
        NzPayInboundIntegrationService integration,
        CancellationToken ct)
    {
        if (!string.Equals(context.Request.Headers["X-NZ-Pay-Contract"],
                NzPayInboundIntegrationService.ContractVersion, StringComparison.Ordinal)) return null;
        return await integration.AuthenticateAsync(context.Request.Headers.Authorization,
            organisationId, scope, ct);
    }

    private static IResult Problem(string detail, int status) => Results.Problem(
        detail: detail,
        statusCode: status,
        title: status == StatusCodes.Status409Conflict ? "Integration request conflict" : "Integration request rejected");
}
