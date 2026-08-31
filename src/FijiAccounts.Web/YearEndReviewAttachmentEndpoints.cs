using System.Security.Claims;
using FijiAccounts.Web.Services;

public static class YearEndReviewAttachmentEndpoints
{
    public static IEndpointRouteBuilder MapYearEndReviewAttachmentEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/o/{organisationId:guid}/periods/{periodId:guid}/review-attachments/{attachmentId:guid}",
                async (
                    Guid organisationId,
                    Guid periodId,
                    Guid attachmentId,
                    YearEndReviewAttachmentService attachments,
                    ClaimsPrincipal principal,
                    CancellationToken cancellationToken) =>
                {
                    var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                    var download = userId is null
                        ? null
                        : await attachments.DownloadAsync(
                            userId,
                            organisationId,
                            periodId,
                            attachmentId,
                            cancellationToken);
                    return download is null
                        ? Results.NotFound()
                        : Results.File(
                            download.Content,
                            download.ContentType,
                            download.FileName,
                            enableRangeProcessing: true);
                })
            .RequireAuthorization();

        return endpoints;
    }
}
