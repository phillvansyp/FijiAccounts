using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class ImmutableDocumentBackfillServiceTests
{
    [Fact]
    public async Task BackfillAsync_MigratesEveryLegacyTypeAndIsIdempotent()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var bill = await test.Purchasing.PostBillAsync(
            test.UserId,
            BillRequest(test));
        var bankAccount = test.Account("1000");
        var contactDocument = LegacyContactDocument(test, [1, 2, 3, 4]);
        var supplierAttachment = LegacySupplierAttachment(
            test,
            bill.Id,
            [5, 6, 7, 8]);
        var bankDocument = LegacyBankDocument(
            test,
            bankAccount.Id,
            [9, 10, 11, 12]);
        test.Db.AddRange(contactDocument, supplierAttachment, bankDocument);
        await test.Db.SaveChangesAsync();

        var store = new DatabaseImmutableDocumentStore(test.Db);
        var service = new ImmutableDocumentBackfillService(test.Db, store);
        var result = await service.BackfillAsync();

        Assert.Equal(1, result.BusinessPartyDocuments);
        Assert.Equal(1, result.SupplierBillAttachments);
        Assert.Equal(1, result.BankStatementDocuments);
        Assert.Equal(3, result.Total);
        Assert.Equal(3, await test.Db.ImmutableDocumentObjects.CountAsync());
        Assert.Equal(3, await test.Db.AuditEvents.CountAsync(x =>
            x.EventType == "ImmutableDocumentBackfilled"));

        var contactObjectId = await test.Db.BusinessPartyDocuments
            .Where(x => x.Id == contactDocument.Id)
            .Select(x => x.ImmutableDocumentObjectId)
            .SingleAsync();
        var supplierObjectId = await test.Db.SupplierBillAttachments
            .Where(x => x.Id == supplierAttachment.Id)
            .Select(x => x.ImmutableDocumentObjectId)
            .SingleAsync();
        var bankObjectId = await test.Db.BankStatementImportDocuments
            .Where(x => x.Id == bankDocument.Id)
            .Select(x => x.ImmutableDocumentObjectId)
            .SingleAsync();
        Assert.Equal([1, 2, 3, 4], await store.ReadVerifiedAsync(
            test.Organisation.Id,
            contactObjectId!.Value));
        Assert.Equal([5, 6, 7, 8], await store.ReadVerifiedAsync(
            test.Organisation.Id,
            supplierObjectId!.Value));
        Assert.Equal([9, 10, 11, 12], await store.ReadVerifiedAsync(
            test.Organisation.Id,
            bankObjectId!.Value));

        var repeated = await service.BackfillAsync();
        Assert.Equal(0, repeated.Total);
        Assert.Equal(3, await test.Db.ImmutableDocumentObjects.CountAsync());
        Assert.Equal(3, await test.Db.AuditEvents.CountAsync(x =>
            x.EventType == "ImmutableDocumentBackfilled"));
    }

    [Fact]
    public async Task BackfillAsync_InvalidLegacyContentRollsBackTheWholeBatch()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var valid = LegacyContactDocument(test, [1, 2, 3, 4]);
        valid.Id = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var invalid = LegacyContactDocument(test, []);
        invalid.Id = Guid.Parse("00000000-0000-0000-0000-000000000002");
        test.Db.AddRange(valid, invalid);
        await test.Db.SaveChangesAsync();

        var service = new ImmutableDocumentBackfillService(
            test.Db,
            new DatabaseImmutableDocumentStore(test.Db));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.BackfillAsync());

        Assert.Contains("no stored content", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await test.Db.ImmutableDocumentObjects.AsNoTracking().ToListAsync());
        Assert.Empty(await test.Db.AuditEvents
            .AsNoTracking()
            .Where(x => x.EventType == "ImmutableDocumentBackfilled")
            .ToListAsync());
        Assert.All(
            await test.Db.BusinessPartyDocuments.AsNoTracking().ToListAsync(),
            x => Assert.Null(x.ImmutableDocumentObjectId));
    }

    private static BusinessPartyDocument LegacyContactDocument(
        AccountingTestDatabase test,
        byte[] content) =>
        new()
        {
            OrganisationId = test.Organisation.Id,
            BusinessPartyId = test.Supplier.Id,
            Type = BusinessPartyDocumentType.Contract,
            Name = "Legacy contract",
            FileName = "legacy-contract.pdf",
            ContentType = "application/pdf",
            OriginalSize = content.LongLength,
            StoredSize = content.LongLength,
            Content = content,
            UploadedByUserId = test.UserId
        };

    private static SupplierBillAttachment LegacySupplierAttachment(
        AccountingTestDatabase test,
        Guid billId,
        byte[] content) =>
        new()
        {
            OrganisationId = test.Organisation.Id,
            SupplierBillId = billId,
            FileName = "legacy-bill.pdf",
            ContentType = "application/pdf",
            OriginalSize = content.LongLength,
            StoredSize = content.LongLength,
            Content = content,
            UploadedByUserId = test.UserId
        };

    private static BankStatementImportDocument LegacyBankDocument(
        AccountingTestDatabase test,
        Guid bankAccountId,
        byte[] content) =>
        new()
        {
            OrganisationId = test.Organisation.Id,
            BankAccountId = bankAccountId,
            ImportBatchId = Guid.NewGuid(),
            FileName = "legacy-statement.pdf",
            ContentType = "application/pdf",
            OriginalSize = content.LongLength,
            Content = content,
            UploadedByUserId = test.UserId
        };

    private static SupplierBillRequest BillRequest(AccountingTestDatabase test) =>
        new(
            test.Organisation.Id,
            test.Supplier.Id,
            "LEGACY-BACKFILL",
            new DateOnly(2026, 8, 23),
            new DateOnly(2026, 9, 22),
            [
                new SupplierBillLineRequest(
                    "Office supplies",
                    1,
                    100,
                    VatTreatment.Standard,
                    test.Account("6500").Id)
            ]);
}
