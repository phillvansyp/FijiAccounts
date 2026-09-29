using System.IO.Compression;
using System.Security.Claims;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;

public static class SupplierBillAttachmentEndpoints
{
    public static IEndpointRouteBuilder MapSupplierBillAttachmentEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/o/{organisationId:guid}/purchases/{billId:guid}/attachments/{attachmentId:guid}",
            async (
                Guid organisationId,
                Guid billId,
                Guid attachmentId,
                SupplierBillAttachmentService attachments,
                ClaimsPrincipal principal,
                CancellationToken cancellationToken) =>
            {
                var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                var attachment = userId is null
                    ? null
                    : await attachments.GetAsync(
                        userId,
                        organisationId,
                        billId,
                        attachmentId,
                        cancellationToken);

                if (attachment is null)
                {
                    return Results.NotFound();
                }

                await attachments.RecordExportAsync(
                    userId!,
                    organisationId,
                    billId,
                    attachment,
                    cancellationToken);

                byte[] content;

                if (attachment.IsCompressed)
                {
                    try
                    {
                        using var input = new MemoryStream(attachment.Content);
                        using var brotli = new BrotliStream(
                            input,
                            CompressionMode.Decompress);

                        using var output =
                            attachment.OriginalSize > 0 &&
                            attachment.OriginalSize <= int.MaxValue
                                ? new MemoryStream((int)attachment.OriginalSize)
                                : new MemoryStream();

                        await brotli.CopyToAsync(output, cancellationToken);
                        content = output.ToArray();
                    }
                    catch (InvalidDataException)
                    {
                        return Results.Problem(
                            "The stored attachment could not be decompressed.");
                    }
                }
                else
                {
                    content = attachment.Content;
                }

                if (attachment.ContentType.Equals(
                        "application/pdf",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var validPdf =
                        content.Length >= 5 &&
                        content[0] == (byte)'%' &&
                        content[1] == (byte)'P' &&
                        content[2] == (byte)'D' &&
                        content[3] == (byte)'F' &&
                        content[4] == (byte)'-';

                    if (!validPdf)
                    {
                        return Results.Problem(
                            "The stored attachment does not contain a valid PDF after decompression.");
                    }
                }

                return Results.File(
                    content,
                    attachment.ContentType,
                    enableRangeProcessing: true);
            })
            .RequireAuthorization();

        endpoints.MapGet(
            "/api/o/{organisationId:guid}/purchases/drafts/{draftId:guid}/attachment",
            async (Guid organisationId, Guid draftId, SupplierBillDraftService drafts,
                ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken) =>
            {
                var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                if (userId is null) return Results.Unauthorized();
                var attachment = await drafts.ReadAttachmentAsync(
                    userId, organisationId, draftId, cancellationToken);
                if (attachment is null) return Results.NotFound();

                byte[] content;
                if (attachment.IsCompressed)
                {
                    try
                    {
                        using var input = new MemoryStream(attachment.Content);
                        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
                        using var output = new MemoryStream();
                        await brotli.CopyToAsync(output, cancellationToken);
                        content = output.ToArray();
                    }
                    catch (InvalidDataException)
                    {
                        return Results.Problem("The stored attachment could not be opened.");
                    }
                }
                else content = attachment.Content;

                if (content.LongLength != attachment.OriginalSize)
                    return Results.Problem("The stored attachment is incomplete.");

                var isPdf = attachment.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) &&
                    content.AsSpan().StartsWith("%PDF-"u8);
                http.Response.Headers.CacheControl = "private, no-store";
                http.Response.Headers.XContentTypeOptions = "nosniff";
                return isPdf
                    ? Results.File(content, "application/pdf", enableRangeProcessing: true)
                    : Results.File(content, "application/octet-stream", attachment.FileName,
                        enableRangeProcessing: true);
            })
            .RequireAuthorization();

        return endpoints;
    }
}
