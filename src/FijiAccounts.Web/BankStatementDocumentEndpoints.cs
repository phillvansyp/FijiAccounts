using System.Security.Claims;
using FijiAccounts.Web.Services;

public static class BankStatementDocumentEndpoints
{
    public static IEndpointRouteBuilder MapBankStatementDocumentEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/o/{organisationId:guid}/banking/imports/{batchId:guid}/statement",
            async (
                Guid organisationId,
                Guid batchId,
                BankStatementImportService statements,
                ClaimsPrincipal principal,
                CancellationToken cancellationToken) =>
            {
                var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                var document = userId is null
                    ? null
                    : await statements.GetDocumentAsync(
                        userId,
                        organisationId,
                        batchId,
                        cancellationToken);

                if (document is null)
                {
                    return Results.NotFound();
                }

                await statements.RecordDocumentExportAsync(
                    userId!,
                    organisationId,
                    batchId,
                    document,
                    cancellationToken);

                return Results.File(
                    document.Content,
                    document.ContentType,
                    enableRangeProcessing: true);
            })
            .RequireAuthorization();

        return endpoints;
    }
}
