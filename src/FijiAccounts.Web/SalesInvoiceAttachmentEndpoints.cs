using System.Security.Claims;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

public static class SalesInvoiceAttachmentEndpoints
{
    public static IEndpointRouteBuilder MapSalesInvoiceAttachmentEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/o/{organisationId:guid}/sales/{invoiceId:guid}/attachments/{attachmentId:guid}",
                async (Guid organisationId, Guid invoiceId, Guid attachmentId,
                    ApplicationDbContext db, TenantAccessService access,
                    IImmutableDocumentStore storage, ClaimsPrincipal principal,
                    HttpContext http, CancellationToken ct) =>
                {
                    var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                    if (userId is null || await access.FindAsync(userId, organisationId) is null)
                        return Results.NotFound();
                    var attachment = await db.SalesInvoiceAttachments.AsNoTracking()
                        .SingleOrDefaultAsync(x =>
                            x.OrganisationId == organisationId &&
                            x.SalesInvoiceId == invoiceId && x.Id == attachmentId, ct);
                    if (attachment is null) return Results.NotFound();
                    var content = await storage.ReadVerifiedAsync(
                        organisationId, attachment.ImmutableDocumentObjectId, ct);
                    if (!content.AsSpan().SequenceEqual(attachment.Content))
                        return Results.Problem("The retained invoice attachment failed its integrity check.");
                    http.Response.Headers.CacheControl = "private, no-store";
                    http.Response.Headers.XContentTypeOptions = "nosniff";
                    return Results.File(content, attachment.ContentType,
                        attachment.FileName, enableRangeProcessing: true);
                })
            .RequireAuthorization();
        return endpoints;
    }
}
