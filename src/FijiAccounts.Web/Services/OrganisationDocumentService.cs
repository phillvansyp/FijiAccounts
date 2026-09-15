using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed class OrganisationDocumentService(ApplicationDbContext db, IImmutableDocumentStore storage)
{
    public const int MaximumBytes = 10 * 1024 * 1024;
    public static readonly string[] Categories = ["Company registration", "VAT registration", "Other"];
    public Task<bool> CanManageAsync(string user, Guid organisation) => db.OrganisationMemberships.AnyAsync(x =>
        x.UserId == user && x.OrganisationId == organisation &&
        (x.Role == OrganisationRole.Owner || x.Role == OrganisationRole.Administrator));

    public async Task<List<OrganisationDocument>> ListAsync(string user, Guid organisation)
    {
        if (!await CanManageAsync(user, organisation)) throw new UnauthorizedAccessException();
        var items = await db.Set<OrganisationDocument>().AsNoTracking().Where(x => x.OrganisationId == organisation).ToListAsync();
        return items.OrderByDescending(x => x.UploadedAt).ToList();
    }

    public async Task<OrganisationDocument> AddAsync(string user, Guid organisation, string category, string filename, byte[] content)
    {
        if (!await CanManageAsync(user, organisation)) throw new UnauthorizedAccessException();
        if (!Categories.Contains(category) || string.IsNullOrWhiteSpace(filename) || filename.Length > 255 ||
            Path.GetFileName(filename) != filename || !filename.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
            content.Length < 5 || content.Length > MaximumBytes || !content.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            throw new InvalidOperationException("Choose a PDF document no larger than 10 MB.");
        var stored = storage.Stage(organisation, user, content);
        var document = new OrganisationDocument { OrganisationId = organisation, Category = category, FileName = filename,
            ContentType = "application/pdf", Size = content.Length, ImmutableDocumentObjectId = stored.Id, UploadedByUserId = user };
        db.Set<OrganisationDocument>().Add(document);
        Audit(user, document, "OrganisationDocumentAdded");
        await db.SaveChangesAsync();
        return document;
    }

    public async Task<byte[]?> ReadAsync(string user, Guid organisation, Guid id)
    {
        if (!await CanManageAsync(user, organisation)) return null;
        var document = await db.Set<OrganisationDocument>().SingleOrDefaultAsync(x => x.OrganisationId == organisation && x.Id == id);
        if (document is null) return null;
        var content = await storage.ReadVerifiedAsync(organisation, document.ImmutableDocumentObjectId);
        Audit(user, document, "OrganisationDocumentOpened");
        await db.SaveChangesAsync();
        return content;
    }

    private void Audit(string user, OrganisationDocument document, string action) => db.AuditEvents.Add(new AuditEvent {
        OrganisationId = document.OrganisationId, UserId = user, EventType = action, EntityType = nameof(OrganisationDocument),
        EntityId = document.Id.ToString(), JsonData = JsonSerializer.Serialize(new { document.Category, document.FileName, document.Size }) });
}
