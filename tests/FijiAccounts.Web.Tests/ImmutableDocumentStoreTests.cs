using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class ImmutableDocumentStoreTests
{
    [Fact]
    public async Task StageAndReadVerifiedAsync_PersistsProviderReferenceAndHash()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var store = new DatabaseImmutableDocumentStore(test.Db);
        byte[] content = [1, 2, 3, 4];

        var reference = store.Stage(
            test.Organisation.Id,
            test.UserId,
            content);
        await test.Db.SaveChangesAsync();

        Assert.Equal(DatabaseImmutableDocumentStore.ProviderName, reference.Provider);
        Assert.Equal(reference.Id.ToString("N"), reference.ObjectKey);
        Assert.Equal(64, reference.Sha256.Length);
        Assert.Equal(content.LongLength, reference.ContentLength);
        Assert.Equal(content, await store.ReadVerifiedAsync(
            test.Organisation.Id,
            reference.Id));
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            store.ReadVerifiedAsync(Guid.NewGuid(), reference.Id));
    }

    [Fact]
    public async Task StoredObject_CannotBeChangedAndTamperingIsDetected()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var store = new DatabaseImmutableDocumentStore(test.Db);
        var reference = store.Stage(
            test.Organisation.Id,
            test.UserId,
            new byte[] { 1, 2, 3, 4 });
        await test.Db.SaveChangesAsync();

        var stored = await test.Db.ImmutableDocumentObjects
            .SingleAsync(x => x.Id == reference.Id);
        stored.Content = [9, 9, 9, 9];
        var immutableError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            test.Db.SaveChangesAsync());
        Assert.Contains("append-only", immutableError.Message, StringComparison.OrdinalIgnoreCase);

        test.Db.Entry(stored).State = EntityState.Detached;
        await test.Db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE ImmutableDocumentObjects SET Content = {new byte[] { 9, 9, 9, 9 }} WHERE Id = {reference.Id}");
        var integrityError = await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.ReadVerifiedAsync(test.Organisation.Id, reference.Id));
        Assert.Contains("integrity", integrityError.Message, StringComparison.OrdinalIgnoreCase);
    }
}
