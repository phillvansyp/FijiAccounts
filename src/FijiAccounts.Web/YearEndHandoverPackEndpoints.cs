using System.Security.Claims;
using FijiAccounts.Web.Services;

public static class YearEndHandoverPackEndpoints
{
    public static IEndpointRouteBuilder MapYearEndHandoverPackEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/o/{organisationId:guid}/periods/{periodId:guid}/handover-pack",
                async (
                    Guid organisationId,
                    Guid periodId,
                    YearEndHandoverPackService packs,
                    ClaimsPrincipal principal,
                    CancellationToken cancellationToken) =>
                {
                    var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    try
                    {
                        var pack = await packs.CreateAsync(
                            userId,
                            organisationId,
                            periodId,
                            cancellationToken);
                        return Results.File(
                            pack.Content,
                            "application/zip",
                            pack.FileName);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        return Results.Forbid();
                    }
                    catch (InvalidOperationException exception)
                    {
                        return Results.BadRequest(new { error = exception.Message });
                    }
                })
            .RequireAuthorization();

        return endpoints;
    }
}
