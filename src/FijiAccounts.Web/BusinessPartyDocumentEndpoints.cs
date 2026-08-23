using System.IO.Compression;
using FijiAccounts.Web.Services;
using System.Security.Claims;

public static class BusinessPartyDocumentEndpoints
{
    public static IEndpointRouteBuilder MapBusinessPartyDocumentEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/o/{organisationId:guid}/contacts/{partyId:guid}/documents/{documentId:guid}",
            async (
                Guid organisationId,
                Guid partyId,
                Guid documentId,
                BusinessPartyDocumentService documents,
                ClaimsPrincipal principal,
                CancellationToken cancellationToken) =>
            {
                var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                var document = userId is null
                    ? null
                    : await documents.GetAsync(
                        userId,
                        organisationId,
                        partyId,
                        documentId,
                        cancellationToken);

                if (document is null)
                {
                    return Results.NotFound();
                }

                byte[] content;

                if (document.IsCompressed)
                {
                    using var input =
                        new MemoryStream(document.Content);

                    using var brotli =
                        new BrotliStream(
                            input,
                            CompressionMode.Decompress);

                    using var output =
                        new MemoryStream();

                    await brotli.CopyToAsync(
                        output,
                        cancellationToken);

                    content =
                        output.ToArray();
                }
                else
                {
                    content =
                        document.Content;
                }

                return Results.File(
                    content,
                    document.ContentType,
                    document.FileName,
                    enableRangeProcessing: true);
            })
            .RequireAuthorization();

        return endpoints;
    }
}
