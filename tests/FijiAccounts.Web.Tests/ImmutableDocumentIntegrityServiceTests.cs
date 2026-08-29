using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class ImmutableDocumentIntegrityServiceTests
{
    [Fact]
    public async Task ScanAsync_RecordsHealthyAppendOnlyReportAndScopesAccess()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var content = new byte[] { 1, 2, 3, 4 };
        var store = new DatabaseImmutableDocumentStore(test.Db);
        var reference = store.Stage(test.Organisation.Id, test.UserId, content);
        test.Db.BusinessPartyDocuments.Add(Document(test, content, reference.Id));
        await test.Db.SaveChangesAsync();
        var service = new ImmutableDocumentIntegrityService(
            test.Db,
            test.Access,
            test.Updates);

        var scan = await service.ScanAsync(test.UserId, test.Organisation.Id);

        Assert.Equal(ImmutableDocumentIntegrityStatus.Healthy, scan.Status);
        Assert.Equal(1, scan.ObjectCount);
        Assert.Equal(1, scan.LinkedDocumentCount);
        Assert.Equal(1, scan.VerifiedObjectCount);
        Assert.Equal(0, scan.IntegrityFailureCount);
        Assert.Equal(0, scan.MissingObjectReferenceCount);
        Assert.Equal(0, scan.LegacyDocumentCount);
        Assert.Equal(0, scan.UnreferencedObjectCount);
        Assert.Equal(scan.Id, (await service.GetLatestAsync(
            test.UserId,
            test.Organisation.Id))!.Id);
        Assert.Null(await service.GetLatestAsync(
            "not-a-member",
            test.Organisation.Id));
        Assert.True(await test.Db.AuditEvents.AnyAsync(x =>
            x.EventType == "ImmutableDocumentIntegrityScanned" &&
            x.EntityId == scan.Id.ToString()));
        Assert.False(await test.Db.Notifications.AnyAsync(x =>
            x.RelatedEntityType == "ImmutableDocumentIntegrity"));

        scan.ObjectCount = 99;
        var immutableError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            test.Db.SaveChangesAsync());
        Assert.Contains("append-only", immutableError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScanAsync_DetectsFailuresAvoidsDuplicateAlertAndResolvesAfterRecovery()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var content = new byte[] { 1, 2, 3, 4 };
        var store = new DatabaseImmutableDocumentStore(test.Db);
        var reference = store.Stage(test.Organisation.Id, test.UserId, content);
        test.Db.BusinessPartyDocuments.Add(Document(test, content, reference.Id));
        var legacy = Document(test, new byte[] { 5, 6, 7, 8 }, null);
        test.Db.BusinessPartyDocuments.Add(legacy);
        await test.Db.SaveChangesAsync();
        await test.Db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE ImmutableDocumentObjects SET Content = {new byte[] { 9, 9, 9, 9 }} WHERE Id = {reference.Id}");
        var service = new ImmutableDocumentIntegrityService(
            test.Db,
            test.Access,
            test.Updates);

        var failed = await service.ScanAsync(test.UserId, test.Organisation.Id);

        Assert.Equal(ImmutableDocumentIntegrityStatus.AttentionRequired, failed.Status);
        Assert.Equal(1, failed.IntegrityFailureCount);
        Assert.Equal(1, failed.LegacyDocumentCount);
        Assert.Single(await test.Db.Notifications.Where(x =>
            x.RelatedEntityType == "ImmutableDocumentIntegrity" &&
            x.Status != NotificationStatus.Resolved).ToListAsync());

        _ = await service.ScanAsync(test.UserId, test.Organisation.Id);
        Assert.Single(await test.Db.Notifications.Where(x =>
            x.RelatedEntityType == "ImmutableDocumentIntegrity" &&
            x.Status != NotificationStatus.Resolved).ToListAsync());

        await test.Db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE ImmutableDocumentObjects SET Content = {content} WHERE Id = {reference.Id}");
        var backfill = new ImmutableDocumentBackfillService(test.Db, store);
        Assert.Equal(1, (await backfill.BackfillAsync()).BusinessPartyDocuments);
        var recovered = await service.ScanAsync(test.UserId, test.Organisation.Id);

        Assert.Equal(ImmutableDocumentIntegrityStatus.Healthy, recovered.Status);
        var notification = await test.Db.Notifications.SingleAsync(x =>
            x.RelatedEntityType == "ImmutableDocumentIntegrity");
        Assert.Equal(NotificationStatus.Resolved, notification.Status);
        Assert.True(notification.IsRead);
        Assert.NotNull(notification.ResolvedAt);
    }

    [Fact]
    public async Task ScanAsync_DetectsCrossTenantObjectReference()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var otherOrganisationId = Guid.NewGuid();
        test.Db.Organisations.Add(new Organisation
        {
            Id = otherOrganisationId,
            LegalName = "Other Limited",
            CountryCode = "FJ",
            BaseCurrency = "FJD",
            TaxLabel = "VAT",
            Kind = OrganisationKind.Business
        });
        var content = new byte[] { 1, 2, 3, 4 };
        var store = new DatabaseImmutableDocumentStore(test.Db);
        var otherReference = store.Stage(otherOrganisationId, test.UserId, content);
        test.Db.BusinessPartyDocuments.Add(Document(
            test,
            content,
            otherReference.Id));
        await test.Db.SaveChangesAsync();
        var service = new ImmutableDocumentIntegrityService(
            test.Db,
            test.Access,
            test.Updates);

        var scan = await service.ScanAsync(test.UserId, test.Organisation.Id);

        Assert.Equal(ImmutableDocumentIntegrityStatus.AttentionRequired, scan.Status);
        Assert.Equal(1, scan.MissingObjectReferenceCount);
    }

    private static BusinessPartyDocument Document(
        AccountingTestDatabase test,
        byte[] content,
        Guid? objectId) =>
        new()
        {
            OrganisationId = test.Organisation.Id,
            BusinessPartyId = test.Supplier.Id,
            Type = BusinessPartyDocumentType.Contract,
            Name = "Integrity evidence",
            FileName = "integrity.pdf",
            ContentType = "application/pdf",
            OriginalSize = content.LongLength,
            StoredSize = content.LongLength,
            Content = content,
            ImmutableDocumentObjectId = objectId,
            UploadedByUserId = test.UserId
        };
}
