using System.IO.Compression;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;
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
                ApplicationDbContext db,
                TenantAccessService access,
                ClaimsPrincipal principal,
                CancellationToken cancellationToken) =>
            {
                var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                if (userId is null ||
                    await access.FindAsync(userId, organisationId) is null)
                {
                    return Results.NotFound();
                }

                var document =
                    await db.BusinessPartyDocuments
                        .AsNoTracking()
                        .SingleOrDefaultAsync(
                            x =>
                                x.Id == documentId &&
                                x.BusinessPartyId == partyId &&
                                x.OrganisationId == organisationId,
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
