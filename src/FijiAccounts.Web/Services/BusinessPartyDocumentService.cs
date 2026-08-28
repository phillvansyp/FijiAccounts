using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record BusinessPartyDocumentUploadRequest(
    Guid OrganisationId,
    Guid BusinessPartyId,
    BusinessPartyDocumentType Type,
    string Name,
    string? Description,
    string FileName,
    string ContentType,
    byte[] Content,
    long OriginalSize,
    bool IsCompressed,
    DateOnly? ExpiryDate);

public sealed class BusinessPartyDocumentService(
    ApplicationDbContext db,
    TenantAccessService access)
{
    public const int MaximumDocumentBytes = 10 * 1024 * 1024;

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

    public async Task<BusinessPartyDocument> AddAsync(
        string userId,
        BusinessPartyDocumentUploadRequest request,
        CancellationToken ct = default)
    {
        var hasAccess =
            await access.CanManageContactsAsync(
                userId,
                request.OrganisationId);

        if (!hasAccess)
        {
            throw new UnauthorizedAccessException();
        }

        var name = request.Name.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        var fileName = request.FileName.Trim();
        var contentType = request.ContentType.Trim().ToLowerInvariant();
        var storedSize = request.Content.LongLength;
        if (!Enum.IsDefined(request.Type) || string.IsNullOrWhiteSpace(name) || name.Length > 200 ||
            description?.Length > 500 || string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255 ||
            Path.GetFileName(fileName) != fileName || string.IsNullOrWhiteSpace(contentType) ||
            contentType.Length > 100 || !AllowedContentTypes.Contains(contentType) ||
            request.OriginalSize <= 0 || request.OriginalSize > MaximumDocumentBytes ||
            storedSize <= 0 || storedSize > MaximumDocumentBytes ||
            (!request.IsCompressed && request.OriginalSize != storedSize) ||
            (request.IsCompressed && storedSize >= request.OriginalSize))
        {
            throw new InvalidOperationException(
                "The document must have valid metadata and non-empty supported content no larger than 10 MB.");
        }

        var party =
            await db.BusinessParties
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == request.BusinessPartyId &&
                        x.OrganisationId == request.OrganisationId,
                    ct);

        if (party is null)
        {
            throw new InvalidOperationException(
                "Business party not found.");
        }

        var document =
            new BusinessPartyDocument
            {
                OrganisationId = request.OrganisationId,
                BusinessPartyId = request.BusinessPartyId,
                Type = request.Type,
                Name = name,
                Description = description,
                FileName = fileName,
                ContentType = contentType,
                Content = request.Content,
                OriginalSize = request.OriginalSize,
                StoredSize = request.Content.LongLength,
                IsCompressed = request.IsCompressed,
                ExpiryDate = request.ExpiryDate,
                UploadedByUserId = userId
            };

        db.BusinessPartyDocuments.Add(document);
        db.AuditEvents.Add(Audit(request.OrganisationId, userId, "BusinessPartyDocumentAdded", document, party.Name));

        await db.SaveChangesAsync(ct);

        return document;
    }


    public async Task<bool> DeleteAsync(
        string userId,
        Guid organisationId,
        Guid documentId,
        CancellationToken ct = default)
    {
        var hasAccess =
            await access.CanManageContactsAsync(
                userId,
                organisationId);

        if (!hasAccess)
        {
            throw new UnauthorizedAccessException();
        }

        var document =
            await db.BusinessPartyDocuments
                .Include(x => x.BusinessParty)
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == documentId &&
                        x.OrganisationId == organisationId,
                    ct);

        if (document is null)
        {
            return false;
        }

        var organisation = await db.Organisations
            .AsNoTracking()
            .SingleAsync(x => x.Id == organisationId, ct);
        var retainUntil = RecordRetentionPolicy.RetainUntil(
            DateOnly.FromDateTime(document.UploadedAtUtc.UtcDateTime),
            organisation.FinancialYearEndMonth,
            organisation.FinancialYearEndDay);

        if (organisation.CountryCode.Equals("FJ", StringComparison.OrdinalIgnoreCase) &&
            RecordRetentionPolicy.IsProtected(retainUntil))
        {
            throw new InvalidOperationException(
                RecordRetentionPolicy.ProtectedMessage(retainUntil));
        }

        db.BusinessPartyDocuments.Remove(document);
        db.AuditEvents.Add(Audit(organisationId, userId, "BusinessPartyDocumentDeleted", document, document.BusinessParty.Name));

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task RecordExportAsync(
        string userId,
        Guid organisationId,
        BusinessPartyDocument document,
        CancellationToken ct = default)
    {
        if (document.OrganisationId != organisationId ||
            await access.FindAsync(userId, organisationId) is null)
        {
            throw new UnauthorizedAccessException();
        }

        db.AuditEvents.Add(Audit(
            organisationId,
            userId,
            "BusinessPartyDocumentExported",
            document,
            "Document export"));
        await db.SaveChangesAsync(ct);
    }

    public async Task<BusinessPartyDocument?> GetAsync(
        string userId,
        Guid organisationId,
        Guid businessPartyId,
        Guid documentId,
        CancellationToken ct = default)
    {
        if (await access.FindAsync(userId, organisationId) is null)
        {
            return null;
        }

        return await db.BusinessPartyDocuments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x =>
                    x.Id == documentId &&
                    x.BusinessPartyId == businessPartyId &&
                    x.OrganisationId == organisationId,
                ct);
    }


    public async Task<List<BusinessPartyDocument>> GetForPartyAsync(
    string userId,
    Guid organisationId,
    Guid businessPartyId,
    CancellationToken ct = default)
{
    if (await access.FindAsync(userId, organisationId) is null)
    {
        throw new UnauthorizedAccessException();
    }

    var documents =
        await db.BusinessPartyDocuments
            .AsNoTracking()
            .Where(
                x =>
                    x.OrganisationId == organisationId &&
                    x.BusinessPartyId == businessPartyId)
            .ToListAsync(ct);

    return documents
        .OrderByDescending(
            x => x.UploadedAtUtc)
        .ToList();
}

    private static AuditEvent Audit(Guid organisationId, string userId, string eventType,
        BusinessPartyDocument document, string partyName) => new()
    {
        OrganisationId = organisationId,
        UserId = userId,
        EventType = eventType,
        EntityType = nameof(BusinessPartyDocument),
        EntityId = document.Id.ToString(),
        JsonData = JsonSerializer.Serialize(new
        {
            document.BusinessPartyId,
            BusinessPartyName = partyName,
            Type = document.Type.ToString(),
            document.Name,
            document.Description,
            document.FileName,
            document.ContentType,
            document.OriginalSize,
            document.StoredSize,
            document.IsCompressed,
            document.ExpiryDate
        })
    };
}
