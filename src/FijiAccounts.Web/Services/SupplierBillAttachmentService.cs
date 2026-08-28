using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed class SupplierBillAttachmentService(
    ApplicationDbContext db,
    TenantAccessService access)
{
    public const int MaximumAttachmentBytes = 10 * 1024 * 1024;

    public async Task<SupplierBillAttachment> AddAsync(
        string userId,
        Guid organisationId,
        Guid supplierBillId,
        SupplierBillAttachmentRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireAccessAsync(userId, organisationId);

        var bill = await db.SupplierBills
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == supplierBillId && x.OrganisationId == organisationId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "The supplier bill was not found for this organisation.");

        var attachment = CreateValidated(
            organisationId,
            bill.Id,
            userId,
            request);

        db.SupplierBillAttachments.Add(attachment);
        db.AuditEvents.Add(AddedAudit(
            organisationId,
            userId,
            bill,
            attachment));
        await db.SaveChangesAsync(cancellationToken);

        return attachment;
    }

    public async Task<SupplierBillAttachment?> GetAsync(
        string userId,
        Guid organisationId,
        Guid supplierBillId,
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        if (await access.FindAsync(userId, organisationId) is null)
        {
            return null;
        }

        return await db.SupplierBillAttachments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x =>
                    x.Id == attachmentId &&
                    x.SupplierBillId == supplierBillId &&
                    x.OrganisationId == organisationId,
                cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        string userId,
        Guid organisationId,
        Guid supplierBillId,
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        await RequireAccessAsync(userId, organisationId);

        var bill = await db.SupplierBills
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == supplierBillId && x.OrganisationId == organisationId,
                cancellationToken);
        if (bill is null)
        {
            return false;
        }

        var attachment = await db.SupplierBillAttachments
            .SingleOrDefaultAsync(
                x =>
                    x.Id == attachmentId &&
                    x.SupplierBillId == supplierBillId &&
                    x.OrganisationId == organisationId,
                cancellationToken);
        if (attachment is null)
        {
            return false;
        }

        var organisation = await db.Organisations
            .AsNoTracking()
            .SingleAsync(x => x.Id == organisationId, cancellationToken);
        var retainUntil = RecordRetentionPolicy.RetainUntil(
            bill.BillDate,
            organisation.FinancialYearEndMonth,
            organisation.FinancialYearEndDay);

        if (organisation.CountryCode.Equals("FJ", StringComparison.OrdinalIgnoreCase) &&
            RecordRetentionPolicy.IsProtected(retainUntil))
        {
            throw new InvalidOperationException(
                RecordRetentionPolicy.ProtectedMessage(retainUntil));
        }

        db.SupplierBillAttachments.Remove(attachment);
        db.AuditEvents.Add(RemovedAudit(
            organisationId,
            userId,
            bill,
            attachment));
        await db.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task RecordExportAsync(
        string userId,
        Guid organisationId,
        Guid supplierBillId,
        SupplierBillAttachment attachment,
        CancellationToken cancellationToken = default)
    {
        if (attachment.OrganisationId != organisationId ||
            attachment.SupplierBillId != supplierBillId ||
            await access.FindAsync(userId, organisationId) is null)
        {
            throw new UnauthorizedAccessException();
        }

        var bill = await db.SupplierBills
            .AsNoTracking()
            .SingleAsync(
                x => x.Id == supplierBillId && x.OrganisationId == organisationId,
                cancellationToken);
        db.AuditEvents.Add(Audit(
            organisationId,
            userId,
            "SupplierBillDocumentExported",
            bill,
            attachment));
        await db.SaveChangesAsync(cancellationToken);
    }

    internal static SupplierBillAttachment CreateValidated(
        Guid organisationId,
        Guid supplierBillId,
        string userId,
        SupplierBillAttachmentRequest request)
    {
        var fileName = request.FileName.Trim();
        var contentType = request.ContentType.Trim().ToLowerInvariant();
        var storedSize = request.Content.LongLength;
        var hasPath = Path.GetFileName(fileName) != fileName;

        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Length > 255 ||
            hasPath ||
            string.IsNullOrWhiteSpace(contentType) ||
            contentType.Length > 100 ||
            request.OriginalSize <= 0 ||
            request.OriginalSize > MaximumAttachmentBytes ||
            storedSize <= 0 ||
            storedSize > MaximumAttachmentBytes ||
            (!request.IsCompressed && request.OriginalSize != storedSize) ||
            (request.IsCompressed && storedSize >= request.OriginalSize))
        {
            throw new InvalidOperationException(
                "The attachment must be a valid, non-empty file no larger than 10 MB.");
        }

        return new SupplierBillAttachment
        {
            OrganisationId = organisationId,
            SupplierBillId = supplierBillId,
            FileName = fileName,
            ContentType = contentType,
            OriginalSize = request.OriginalSize,
            StoredSize = storedSize,
            IsCompressed = request.IsCompressed,
            Content = request.Content,
            UploadedByUserId = userId
        };
    }

    internal static AuditEvent AddedAudit(
        Guid organisationId,
        string userId,
        SupplierBill bill,
        SupplierBillAttachment attachment) =>
        Audit(
            organisationId,
            userId,
            "SupplierBillDocumentAdded",
            bill,
            attachment);

    private static AuditEvent RemovedAudit(
        Guid organisationId,
        string userId,
        SupplierBill bill,
        SupplierBillAttachment attachment) =>
        Audit(
            organisationId,
            userId,
            "SupplierBillDocumentRemoved",
            bill,
            attachment);

    private async Task RequireAccessAsync(string userId, Guid organisationId)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot manage supplier bill attachments for this organisation.");
        }
    }

    private static AuditEvent Audit(
        Guid organisationId,
        string userId,
        string eventType,
        SupplierBill bill,
        SupplierBillAttachment attachment) =>
        new()
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = eventType,
            EntityType = nameof(SupplierBill),
            EntityId = bill.Id.ToString(),
            JsonData = JsonSerializer.Serialize(new
            {
                AttachmentId = attachment.Id,
                bill.BillNumber,
                attachment.FileName,
                attachment.ContentType,
                attachment.OriginalSize,
                attachment.StoredSize,
                attachment.IsCompressed
            })
        };
}
