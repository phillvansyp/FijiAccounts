using System.Security.Cryptography;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record ImmutableDocumentReference(
    Guid Id,
    string Provider,
    string ObjectKey,
    string Sha256,
    long ContentLength);

public interface IImmutableDocumentStore
{
    ImmutableDocumentReference Stage(
        Guid organisationId,
        string userId,
        ReadOnlyMemory<byte> content);

    Task<byte[]> ReadVerifiedAsync(
        Guid organisationId,
        Guid objectId,
        CancellationToken cancellationToken = default);
}

public sealed class DatabaseImmutableDocumentStore(ApplicationDbContext db)
    : IImmutableDocumentStore
{
    public const string ProviderName = "database";

    public ImmutableDocumentReference Stage(
        Guid organisationId,
        string userId,
        ReadOnlyMemory<byte> content)
    {
        if (organisationId == Guid.Empty || string.IsNullOrWhiteSpace(userId) || content.IsEmpty)
        {
            throw new InvalidOperationException(
                "Immutable document content and ownership are required.");
        }

        var id = Guid.NewGuid();
        var hash = Convert.ToHexString(SHA256.HashData(content.Span));
        var stored = new ImmutableDocumentObject
        {
            Id = id,
            OrganisationId = organisationId,
            Provider = ProviderName,
            ObjectKey = id.ToString("N"),
            Sha256 = hash,
            ContentLength = content.Length,
            Content = content.ToArray(),
            CreatedByUserId = userId
        };

        db.ImmutableDocumentObjects.Add(stored);
        return new(id, stored.Provider, stored.ObjectKey, hash, stored.ContentLength);
    }

    public async Task<byte[]> ReadVerifiedAsync(
        Guid organisationId,
        Guid objectId,
        CancellationToken cancellationToken = default)
    {
        var stored = await db.ImmutableDocumentObjects
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == objectId && x.OrganisationId == organisationId,
                cancellationToken)
            ?? throw new FileNotFoundException(
                "The retained document object was not found.");

        var actualHash = Convert.ToHexString(SHA256.HashData(stored.Content));
        if (stored.Content.LongLength != stored.ContentLength ||
            !actualHash.Equals(stored.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The retained document failed its integrity check.");
        }

        return stored.Content;
    }
}
