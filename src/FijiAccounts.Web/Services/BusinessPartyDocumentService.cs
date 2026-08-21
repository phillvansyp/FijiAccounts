using System.IO.Compression;
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
                Name = request.Name,
                Description = request.Description,
                FileName = request.FileName,
                ContentType = request.ContentType,
                Content = request.Content,
                OriginalSize = request.OriginalSize,
                StoredSize = request.Content.LongLength,
                IsCompressed = request.IsCompressed,
                ExpiryDate = request.ExpiryDate,
                UploadedByUserId = userId
            };

        db.BusinessPartyDocuments.Add(document);

        await db.SaveChangesAsync(ct);
        var saved =
            await db.BusinessPartyDocuments
                .AsNoTracking()
                .Where(
                    x => x.Id == document.Id)
                .Select(
                    x => new
                    {
                        x.Id,
                        x.BusinessPartyId,
                        x.OrganisationId,
                        x.Name
                    })
                .SingleOrDefaultAsync(ct);

        Console.WriteLine(
            $"[DOCUMENT DEBUG] Saved: {saved?.Name ?? "NOT FOUND"} | Party: {saved?.BusinessPartyId} | Org: {saved?.OrganisationId}");


        return document;
    }


    public async Task DeleteAsync(
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
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == documentId &&
                        x.OrganisationId == organisationId,
                    ct);

        if (document is null)
        {
            return;
        }

        db.BusinessPartyDocuments.Remove(document);

        await db.SaveChangesAsync(ct);
    }


    public async Task<List<BusinessPartyDocument>> GetForPartyAsync(
    Guid organisationId,
    Guid businessPartyId,
    CancellationToken ct = default)
{
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
}
