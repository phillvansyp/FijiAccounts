using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record OrganisationLogoUploadRequest(
    Guid OrganisationId,
    string FileName,
    string ContentType,
    byte[] Content);

public sealed class OrganisationBrandingService(
    ApplicationDbContext db,
    TenantAccessService access)
{
    public const int MaximumLogoBytes = 1024 * 1024;

    public async Task<OrganisationBranding?> GetAsync(
        string userId,
        Guid organisationId,
        CancellationToken ct = default)
    {
        if (await access.FindAsync(userId, organisationId) is null)
        {
            throw new UnauthorizedAccessException();
        }

        return await db.OrganisationBrandings
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganisationId == organisationId, ct);
    }

    public async Task<OrganisationBranding> SaveAsync(
        string userId,
        OrganisationLogoUploadRequest request,
        CancellationToken ct = default)
    {
        await RequireManagerAsync(userId, request.OrganisationId, ct);
        var fileName = request.FileName.Trim();
        var contentType = request.ContentType.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255 ||
            Path.GetFileName(fileName) != fileName ||
            request.Content.Length is < 1 or > MaximumLogoBytes ||
            !HasValidSignature(contentType, request.Content))
        {
            throw new InvalidOperationException(
                "Choose a valid PNG, JPEG or WebP logo no larger than 1 MB.");
        }

        var branding = await db.OrganisationBrandings.SingleOrDefaultAsync(
            x => x.OrganisationId == request.OrganisationId,
            ct);
        var replaced = branding is not null;
        if (branding is null)
        {
            branding = new OrganisationBranding
            {
                OrganisationId = request.OrganisationId,
                LogoFileName = fileName,
                LogoContentType = contentType,
                LogoContent = request.Content,
                UploadedByUserId = userId
            };
            db.OrganisationBrandings.Add(branding);
        }
        else
        {
            branding.LogoFileName = fileName;
            branding.LogoContentType = contentType;
            branding.LogoContent = request.Content;
            branding.UploadedAt = DateTimeOffset.UtcNow;
            branding.UploadedByUserId = userId;
        }

        db.AuditEvents.Add(Audit(
            request.OrganisationId,
            userId,
            replaced ? "OrganisationLogoReplaced" : "OrganisationLogoAdded",
            new { FileName = fileName, ContentType = contentType, Size = request.Content.Length }));
        await db.SaveChangesAsync(ct);
        return branding;
    }

    public async Task<bool> DeleteAsync(
        string userId,
        Guid organisationId,
        CancellationToken ct = default)
    {
        await RequireManagerAsync(userId, organisationId, ct);
        var branding = await db.OrganisationBrandings.SingleOrDefaultAsync(
            x => x.OrganisationId == organisationId,
            ct);
        if (branding is null) return false;

        db.OrganisationBrandings.Remove(branding);
        db.AuditEvents.Add(Audit(
            organisationId,
            userId,
            "OrganisationLogoRemoved",
            new { branding.LogoFileName, branding.LogoContentType, Size = branding.LogoContent.Length }));
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static bool HasValidSignature(string contentType, byte[] content) =>
        contentType switch
        {
            "image/png" => content.Length >= 8 && content.AsSpan(0, 8)
                .SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            "image/jpeg" => content.Length >= 4 && content[0] == 0xFF &&
                content[1] == 0xD8 && content[2] == 0xFF && content[^2] == 0xFF && content[^1] == 0xD9,
            "image/webp" => content.Length >= 12 &&
                content.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
                content.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };

    private async Task RequireManagerAsync(
        string userId,
        Guid organisationId,
        CancellationToken ct)
    {
        if (!await access.CanManageTeamAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException(
                "You do not have permission to change this company's logo.");
        }
    }

    private static AuditEvent Audit(
        Guid organisationId,
        string userId,
        string eventType,
        object evidence) =>
        new()
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = eventType,
            EntityType = nameof(Organisation),
            EntityId = organisationId.ToString(),
            JsonData = JsonSerializer.Serialize(evidence)
        };
}
