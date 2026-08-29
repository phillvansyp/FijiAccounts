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
    : IImmutableDocumentStore, IImmutableDocumentProviderDiagnostics
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

    public async Task<ImmutableDocumentProviderHealth> ProbeAsync(
        CancellationToken cancellationToken = default)
    {
        var available = await db.Database.CanConnectAsync(cancellationToken);
        return new(
            ProviderName,
            "Database compatibility provider",
            available,
            available
                ? "The application database is available."
                : "The application database is unavailable.",
            new(
                ApplicationAppendOnly: true,
                IntegrityVerification: true,
                TenantIsolation: true,
                NativeRetentionLock: false,
                ConfiguredRetentionYears: null));
    }
}
