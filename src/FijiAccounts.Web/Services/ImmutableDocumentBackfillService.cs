using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record ImmutableDocumentBackfillResult(
    int BusinessPartyDocuments,
    int SupplierBillAttachments,
    int BankStatementDocuments)
{
    public int Total =>
        BusinessPartyDocuments + SupplierBillAttachments + BankStatementDocuments;
}

public sealed class ImmutableDocumentBackfillService(
    ApplicationDbContext db,
    IImmutableDocumentStore storage)
{
    private const int BatchSize = 100;
    private const string SystemUserId = "system:immutable-document-backfill";

    public async Task<ImmutableDocumentBackfillResult> BackfillAsync(
        CancellationToken cancellationToken = default)
    {
        var businessPartyDocuments = await BackfillBusinessPartyDocumentsAsync(
            cancellationToken);
        var supplierBillAttachments = await BackfillSupplierBillAttachmentsAsync(
            cancellationToken);
        var bankStatementDocuments = await BackfillBankStatementDocumentsAsync(
            cancellationToken);

        return new(
            businessPartyDocuments,
            supplierBillAttachments,
            bankStatementDocuments);
    }

    private async Task<int> BackfillBusinessPartyDocumentsAsync(
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            var documents = await db.BusinessPartyDocuments
                .Where(x => x.ImmutableDocumentObjectId == null)
                .OrderBy(x => x.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (documents.Count == 0)
            {
                return total;
            }

            await BackfillBatchAsync(
                documents,
                x => x.Id,
                x => x.OrganisationId,
                x => x.Content,
                (x, objectId) => x.ImmutableDocumentObjectId = objectId,
                nameof(BusinessPartyDocument),
                cancellationToken);
            total += documents.Count;
        }
    }

    private async Task<int> BackfillSupplierBillAttachmentsAsync(
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            var documents = await db.SupplierBillAttachments
                .Where(x => x.ImmutableDocumentObjectId == null)
                .OrderBy(x => x.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (documents.Count == 0)
            {
                return total;
            }

            await BackfillBatchAsync(
                documents,
                x => x.Id,
                x => x.OrganisationId,
                x => x.Content,
                (x, objectId) => x.ImmutableDocumentObjectId = objectId,
                nameof(SupplierBillAttachment),
                cancellationToken);
            total += documents.Count;
        }
    }

    private async Task<int> BackfillBankStatementDocumentsAsync(
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            var documents = await db.BankStatementImportDocuments
                .Where(x => x.ImmutableDocumentObjectId == null)
                .OrderBy(x => x.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (documents.Count == 0)
            {
                return total;
            }

            await BackfillBatchAsync(
                documents,
                x => x.Id,
                x => x.OrganisationId,
                x => x.Content,
                (x, objectId) => x.ImmutableDocumentObjectId = objectId,
                nameof(BankStatementImportDocument),
                cancellationToken);
            total += documents.Count;
        }
    }

    private async Task BackfillBatchAsync<TDocument>(
        IReadOnlyList<TDocument> documents,
        Func<TDocument, Guid> id,
        Func<TDocument, Guid> organisationId,
        Func<TDocument, byte[]> content,
        Action<TDocument, Guid> link,
        string entityType,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            cancellationToken);
        try
        {
            var references = new List<(Guid OrganisationId, ImmutableDocumentReference Reference)>(
                documents.Count);

            foreach (var document in documents)
            {
                var bytes = content(document);
                if (bytes.Length == 0)
                {
                    throw new InvalidDataException(
                        $"Legacy {entityType} {id(document)} has no stored content.");
                }

                var ownerId = organisationId(document);
                var reference = storage.Stage(ownerId, SystemUserId, bytes);
                link(document, reference.Id);
                references.Add((ownerId, reference));
                db.AuditEvents.Add(new AuditEvent
                {
                    OrganisationId = ownerId,
                    UserId = SystemUserId,
                    EventType = "ImmutableDocumentBackfilled",
                    EntityType = entityType,
                    EntityId = id(document).ToString(),
                    JsonData = JsonSerializer.Serialize(new
                    {
                        ImmutableDocumentObjectId = reference.Id,
                        reference.Provider,
                        reference.ObjectKey,
                        reference.Sha256,
                        reference.ContentLength
                    })
                });
            }

            await db.SaveChangesAsync(cancellationToken);
            foreach (var item in references)
            {
                _ = await storage.ReadVerifiedAsync(
                    item.OrganisationId,
                    item.Reference.Id,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
