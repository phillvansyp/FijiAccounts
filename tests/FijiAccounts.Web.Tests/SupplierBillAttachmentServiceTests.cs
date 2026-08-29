using System.Text.Json;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SupplierBillAttachmentServiceTests
{
    [Fact]
    public async Task AddAndDeleteAsync_PersistProtectAndAuditWithoutContent()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var bill = await PostBillAsync(test, "SUP-ATTACH-1");
        var service = new SupplierBillAttachmentService(test.Db, test.Access);

        var attachment = await service.AddAsync(
            test.UserId,
            test.Organisation.Id,
            bill.Id,
            Attachment());

        var stored = await test.Db.SupplierBillAttachments
            .AsNoTracking()
            .SingleAsync(x => x.Id == attachment.Id);
        Assert.Equal("invoice.pdf", stored.FileName);
        Assert.Equal("application/pdf", stored.ContentType);
        Assert.Equal(4, stored.OriginalSize);
        Assert.Equal(4, stored.StoredSize);
        Assert.False(stored.IsCompressed);
        Assert.Equal([1, 2, 3, 4], stored.Content);
        Assert.NotNull(stored.ImmutableDocumentObjectId);
        Assert.True(await test.Db.ImmutableDocumentObjects.AnyAsync(x =>
            x.Id == stored.ImmutableDocumentObjectId &&
            x.OrganisationId == test.Organisation.Id));

        var protectedError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteAsync(
                test.UserId,
                test.Organisation.Id,
                bill.Id,
                attachment.Id));
        Assert.Contains("seven-year", protectedError.Message, StringComparison.OrdinalIgnoreCase);

        bill.BillDate = new DateOnly(2017, 8, 23);
        test.Db.SupplierBills.Update(bill);
        await test.Db.SaveChangesAsync();
        Assert.True(await service.DeleteAsync(
            test.UserId,
            test.Organisation.Id,
            bill.Id,
            attachment.Id));
        Assert.False(await test.Db.SupplierBillAttachments
            .AnyAsync(x => x.Id == attachment.Id));

        var audits = await test.Db.AuditEvents
            .AsNoTracking()
            .Where(x =>
                x.EntityId == bill.Id.ToString() &&
                (x.EventType == "SupplierBillDocumentAdded" ||
                 x.EventType == "SupplierBillDocumentRemoved"))
            .OrderBy(x => x.Id)
            .ToListAsync();
        Assert.Equal(
            ["SupplierBillDocumentAdded", "SupplierBillDocumentRemoved"],
            audits.Select(x => x.EventType));
        Assert.All(audits, audit =>
        {
            Assert.Equal(nameof(SupplierBill), audit.EntityType);
            Assert.Equal(test.UserId, audit.UserId);
            using var evidence = JsonDocument.Parse(audit.JsonData);
            Assert.Equal(
                bill.BillNumber,
                evidence.RootElement.GetProperty("BillNumber").GetString());
            Assert.Equal(
                attachment.Id.ToString(),
                evidence.RootElement.GetProperty("AttachmentId").GetString());
            Assert.Equal(4, evidence.RootElement.GetProperty("OriginalSize").GetInt64());
            Assert.Equal(4, evidence.RootElement.GetProperty("StoredSize").GetInt64());
            Assert.False(evidence.RootElement.GetProperty("IsCompressed").GetBoolean());
            Assert.False(evidence.RootElement.TryGetProperty("Content", out _));
        });
    }

    [Fact]
    public async Task InvalidAndMissingAdds_CreateNoAttachmentOrAudit()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var bill = await PostBillAsync(test, "SUP-ATTACH-2");
        var service = new SupplierBillAttachmentService(test.Db, test.Access);
        var initialAuditCount = await test.Db.AuditEvents.CountAsync();

        var invalidRequests = new[]
        {
            Attachment() with { FileName = " " },
            Attachment() with { FileName = "folder/invoice.pdf" },
            Attachment() with { ContentType = " " },
            Attachment() with { OriginalSize = 0 },
            Attachment() with { OriginalSize = 5 },
            Attachment() with { IsCompressed = true },
            Attachment() with { Content = [] },
            Attachment() with
            {
                OriginalSize = SupplierBillAttachmentService.MaximumAttachmentBytes + 1L
            },
            Attachment() with
            {
                OriginalSize = SupplierBillAttachmentService.MaximumAttachmentBytes,
                Content = new byte[SupplierBillAttachmentService.MaximumAttachmentBytes + 1]
            }
        };

        foreach (var request in invalidRequests)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AddAsync(
                    test.UserId,
                    test.Organisation.Id,
                    bill.Id,
                    request));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddAsync(
                test.UserId,
                test.Organisation.Id,
                Guid.NewGuid(),
                Attachment()));

        Assert.Empty(await test.Db.SupplierBillAttachments.AsNoTracking().ToListAsync());
        Assert.Equal(initialAuditCount, await test.Db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_DoesNotApplyFijiRetentionRuleToAnotherJurisdiction()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.CountryCode = "NZ";
        await test.Db.SaveChangesAsync();
        var bill = await PostBillAsync(test, "SUP-NZ-RETENTION");
        var service = new SupplierBillAttachmentService(test.Db, test.Access);
        var attachment = await service.AddAsync(
            test.UserId,
            test.Organisation.Id,
            bill.Id,
            Attachment());

        Assert.True(await service.DeleteAsync(
            test.UserId,
            test.Organisation.Id,
            bill.Id,
            attachment.Id));
    }

    [Fact]
    public async Task ReadOnlyAndCrossTenantAttempts_CreateNoAuditNoise()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var bill = await PostBillAsync(test, "SUP-ATTACH-3");
        var service = new SupplierBillAttachmentService(test.Db, test.Access);
        await test.Db.OrganisationMemberships
            .Where(x =>
                x.UserId == test.UserId &&
                x.OrganisationId == test.Organisation.Id)
            .ExecuteUpdateAsync(update =>
                update.SetProperty(x => x.Role, OrganisationRole.ReadOnly));
        var initialAuditCount = await test.Db.AuditEvents.CountAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.AddAsync(
                test.UserId,
                test.Organisation.Id,
                bill.Id,
                Attachment()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.DeleteAsync(
                test.UserId,
                test.Organisation.Id,
                bill.Id,
                Guid.NewGuid()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.AddAsync(
                test.UserId,
                Guid.NewGuid(),
                bill.Id,
                Attachment()));

        Assert.Empty(await test.Db.SupplierBillAttachments.AsNoTracking().ToListAsync());
        Assert.Equal(initialAuditCount, await test.Db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_ScopesAttachmentToRequestedBill()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var firstBill = await PostBillAsync(test, "SUP-ATTACH-4");
        var secondBill = await PostBillAsync(test, "SUP-ATTACH-5");
        var service = new SupplierBillAttachmentService(test.Db, test.Access);
        var attachment = await service.AddAsync(
            test.UserId,
            test.Organisation.Id,
            firstBill.Id,
            Attachment());
        var initialAuditCount = await test.Db.AuditEvents.CountAsync();

        Assert.False(await service.DeleteAsync(
            test.UserId,
            test.Organisation.Id,
            secondBill.Id,
            attachment.Id));

        Assert.True(await test.Db.SupplierBillAttachments
            .AnyAsync(x => x.Id == attachment.Id));
        Assert.Equal(initialAuditCount, await test.Db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task GetAsync_AllowsReadOnlyAccessAndScopesEveryRouteIdentifier()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var bill = await PostBillAsync(test, "SUP-DOWNLOAD");
        var otherBill = await PostBillAsync(test, "SUP-DOWNLOAD-OTHER");
        var service = new SupplierBillAttachmentService(test.Db, test.Access);
        var attachment = await service.AddAsync(
            test.UserId,
            test.Organisation.Id,
            bill.Id,
            Attachment());
        await test.Db.OrganisationMemberships
            .Where(x =>
                x.UserId == test.UserId &&
                x.OrganisationId == test.Organisation.Id)
            .ExecuteUpdateAsync(update =>
                update.SetProperty(x => x.Role, OrganisationRole.ReadOnly));

        var downloaded = await service.GetAsync(
            test.UserId,
            test.Organisation.Id,
            bill.Id,
            attachment.Id);
        Assert.NotNull(downloaded);
        Assert.Equal(attachment.Id, downloaded.Id);
        Assert.Equal([1, 2, 3, 4], downloaded.Content);
        await service.RecordExportAsync(
            test.UserId,
            test.Organisation.Id,
            bill.Id,
            downloaded);
        Assert.True(await test.Db.AuditEvents.AnyAsync(x =>
            x.EntityId == bill.Id.ToString() &&
            x.EventType == "SupplierBillDocumentExported"));
        Assert.Null(await service.GetAsync(
            test.UserId,
            test.Organisation.Id,
            otherBill.Id,
            attachment.Id));
        Assert.Null(await service.GetAsync(
            test.UserId,
            Guid.NewGuid(),
            bill.Id,
            attachment.Id));
        Assert.Null(await service.GetAsync(
            "not-a-member",
            test.Organisation.Id,
            bill.Id,
            attachment.Id));
    }

    [Fact]
    public async Task AuthorizedOtherTenant_CannotTargetThisTenantsBill()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var bill = await PostBillAsync(test, "SUP-ATTACH-CROSS-TENANT");
        var otherOrganisation = new Organisation
        {
            LegalName = "Other Organisation Limited",
            TradingName = "Other Organisation",
            CountryCode = "FJ",
            BaseCurrency = "FJD",
            TaxLabel = "VAT",
            Kind = OrganisationKind.Business
        };
        test.Db.Organisations.Add(otherOrganisation);
        test.Db.OrganisationMemberships.Add(new OrganisationMembership
        {
            OrganisationId = otherOrganisation.Id,
            Organisation = otherOrganisation,
            UserId = test.UserId,
            Role = OrganisationRole.Owner
        });
        await test.Db.SaveChangesAsync();
        var service = new SupplierBillAttachmentService(test.Db, test.Access);
        var initialAuditCount = await test.Db.AuditEvents.CountAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddAsync(
                test.UserId,
                otherOrganisation.Id,
                bill.Id,
                Attachment()));
        Assert.False(await service.DeleteAsync(
            test.UserId,
            otherOrganisation.Id,
            bill.Id,
            Guid.NewGuid()));

        Assert.Empty(await test.Db.SupplierBillAttachments.AsNoTracking().ToListAsync());
        Assert.Equal(initialAuditCount, await test.Db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task PostBillAsync_InvalidAttachmentDoesNotPostBillOrAudit()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            test.Purchasing.PostBillAsync(
                test.UserId,
                BillRequest(test, "SUP-ATOMIC"),
                Attachment() with { Content = [] }));

        Assert.Empty(await test.Db.SupplierBills.AsNoTracking().ToListAsync());
        Assert.Empty(await test.Db.SupplierBillAttachments.AsNoTracking().ToListAsync());
        Assert.Empty(await test.Db.AuditEvents.AsNoTracking().ToListAsync());
        Assert.Empty(await test.Db.PostedJournals.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task PostBillAsync_AtomicallyAddsAttachmentAndAudit()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();

        var bill = await test.Purchasing.PostBillAsync(
            test.UserId,
            BillRequest(test, "SUP-ATOMIC-OK"),
            Attachment());

        var attachment = await test.Db.SupplierBillAttachments
            .AsNoTracking()
            .SingleAsync(x => x.SupplierBillId == bill.Id);
        Assert.Equal(test.Organisation.Id, attachment.OrganisationId);
        Assert.Single(await test.Db.AuditEvents
            .Where(x =>
                x.EntityId == bill.Id.ToString() &&
                x.EventType == "SupplierBillDocumentAdded")
            .ToListAsync());
    }

    private static SupplierBillAttachmentRequest Attachment() =>
        new(
            "invoice.pdf",
            "application/pdf",
            4,
            [1, 2, 3, 4],
            false);

    private static Task<SupplierBill> PostBillAsync(
        AccountingTestDatabase test,
        string reference) =>
        test.Purchasing.PostBillAsync(
            test.UserId,
            BillRequest(test, reference));

    private static SupplierBillRequest BillRequest(
        AccountingTestDatabase test,
        string reference) =>
        new(
            test.Organisation.Id,
            test.Supplier.Id,
            reference,
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
