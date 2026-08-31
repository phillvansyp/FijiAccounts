using System.IO.Compression;
using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record YearEndReviewAttachmentRequest(
    string FileName,
    string ContentType,
    long OriginalSize,
    byte[] Content,
    bool IsCompressed);

public sealed record YearEndReviewAttachmentDownload(
    byte[] Content,
    string FileName,
    string ContentType);

public sealed class YearEndReviewAttachmentService(
    ApplicationDbContext db,
    TenantAccessService access,
    IImmutableDocumentStore storage)
{
    public const int MaximumAttachmentBytes = 10 * 1024 * 1024;

    private static readonly HashSet<string> AllowedContentTypes =
    [
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "image/jpeg",
        "image/png"
    ];

    public async Task<YearEndReviewAttachment> AddAsync(
        string userId,
        Guid organisationId,
        Guid periodId,
        YearEndReviewArea area,
        YearEndReviewAttachmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var item = await db.YearEndReviewItems
            .Include(x => x.YearEndReview)
            .ThenInclude(x => x.AccountingPeriod)
            .SingleOrDefaultAsync(
                x => x.YearEndReview.OrganisationId == organisationId &&
                     x.YearEndReview.AccountingPeriodId == periodId &&
                     x.Area == area,
                cancellationToken)
            ?? throw new InvalidOperationException("The year-end review item was not found.");

        EnsureOpen(item.YearEndReview);
        var canManage = await access.CanManageTeamAsync(userId, organisationId);
        if (!canManage &&
            (item.Status != YearEndReviewStatus.QueryRaised ||
             item.QueryAssignedToUserId != userId))
        {
            throw new UnauthorizedAccessException(
                "Only review managers or the assigned query owner can add supporting evidence.");
        }

        var fileName = request.FileName.Trim();
        var contentType = request.ContentType.Trim().ToLowerInvariant();
        var storedSize = request.Content.LongLength;
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Length > 255 ||
            Path.GetFileName(fileName) != fileName ||
            !AllowedContentTypes.Contains(contentType) ||
            request.OriginalSize <= 0 ||
            request.OriginalSize > MaximumAttachmentBytes ||
            storedSize <= 0 ||
            storedSize > MaximumAttachmentBytes ||
            (!request.IsCompressed && request.OriginalSize != storedSize) ||
            (request.IsCompressed && storedSize >= request.OriginalSize))
        {
            throw new InvalidOperationException(
                "Upload a supported, non-empty PDF, Word, Excel, JPEG or PNG file no larger than 10 MB.");
        }

        var original = RestoreAndValidate(
            request.Content,
            request.IsCompressed,
            request.OriginalSize,
            contentType);
        if (original.LongLength != request.OriginalSize)
        {
            throw new InvalidOperationException("The supporting document size is invalid.");
        }

        var storedObject = storage.Stage(organisationId, userId, request.Content);
        var attachment = new YearEndReviewAttachment
        {
            OrganisationId = organisationId,
            YearEndReviewItemId = item.Id,
            FileName = fileName,
            ContentType = contentType,
            OriginalSize = request.OriginalSize,
            StoredSize = storedSize,
            IsCompressed = request.IsCompressed,
            ImmutableDocumentObjectId = storedObject.Id,
            UploadedByUserId = userId
        };
        db.YearEndReviewAttachments.Add(attachment);
        db.AuditEvents.Add(Audit(
            organisationId,
            userId,
            "YearEndReviewAttachmentAdded",
            periodId,
            area,
            attachment));
        await db.SaveChangesAsync(cancellationToken);
        return attachment;
    }

    public async Task<YearEndReviewAttachmentDownload?> DownloadAsync(
        string userId,
        Guid organisationId,
        Guid periodId,
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        var attachment = await db.YearEndReviewAttachments.AsNoTracking()
            .Include(x => x.YearEndReviewItem)
            .ThenInclude(x => x.YearEndReview)
            .SingleOrDefaultAsync(
                x => x.Id == attachmentId &&
                     x.OrganisationId == organisationId &&
                     x.YearEndReviewItem.YearEndReview.AccountingPeriodId == periodId,
                cancellationToken);
        if (attachment is null)
        {
            return null;
        }

        var canManage = await access.CanManageTeamAsync(userId, organisationId);
        if (!canManage && attachment.YearEndReviewItem.QueryAssignedToUserId != userId)
        {
            return null;
        }

        var stored = await storage.ReadVerifiedAsync(
            organisationId,
            attachment.ImmutableDocumentObjectId,
            cancellationToken);
        var content = RestoreAndValidate(
            stored,
            attachment.IsCompressed,
            attachment.OriginalSize,
            attachment.ContentType);
        db.AuditEvents.Add(Audit(
            organisationId,
            userId,
            "YearEndReviewAttachmentDownloaded",
            periodId,
            attachment.YearEndReviewItem.Area,
            attachment));
        await db.SaveChangesAsync(cancellationToken);
        return new(content, attachment.FileName, attachment.ContentType);
    }

    internal static byte[] RestoreAndValidate(
        byte[] stored,
        bool isCompressed,
        long originalSize,
        string contentType)
    {
        byte[] content;
        if (isCompressed)
        {
            try
            {
                using var input = new MemoryStream(stored);
                using var brotli = new BrotliStream(input, CompressionMode.Decompress);
                using var output = originalSize <= int.MaxValue
                    ? new MemoryStream((int)originalSize)
                    : new MemoryStream();
                brotli.CopyTo(output);
                content = output.ToArray();
            }
            catch (InvalidDataException)
            {
                throw new InvalidDataException(
                    "The retained supporting document could not be decompressed.");
            }
        }
        else
        {
            content = stored;
        }

        if (content.LongLength != originalSize)
        {
            throw new InvalidDataException(
                "The retained supporting document size does not match its evidence record.");
        }

        if (contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) &&
            (content.Length < 5 ||
             content[0] != (byte)'%' ||
             content[1] != (byte)'P' ||
             content[2] != (byte)'D' ||
             content[3] != (byte)'F' ||
             content[4] != (byte)'-'))
        {
            throw new InvalidDataException(
                "The supporting document does not contain a valid PDF.");
        }

        return content;
    }

    private static void EnsureOpen(YearEndReview review)
    {
        if (review.AccountingPeriod.IsLocked || review.ApprovedAt is not null)
        {
            throw new InvalidOperationException(
                "Supporting evidence can only be added while the year-end review is open.");
        }
    }

    private static AuditEvent Audit(
        Guid organisationId,
        string userId,
        string eventType,
        Guid periodId,
        YearEndReviewArea area,
        YearEndReviewAttachment attachment) =>
        new()
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = eventType,
            EntityType = nameof(AccountingPeriod),
            EntityId = periodId.ToString(),
            JsonData = JsonSerializer.Serialize(new
            {
                Area = area,
                AttachmentId = attachment.Id,
                attachment.FileName,
                attachment.ContentType,
                attachment.OriginalSize,
                attachment.StoredSize,
                attachment.IsCompressed,
                attachment.ImmutableDocumentObjectId
            })
        };
}
