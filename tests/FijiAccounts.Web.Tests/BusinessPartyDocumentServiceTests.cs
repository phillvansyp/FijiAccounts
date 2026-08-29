using System.Text.Json;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class BusinessPartyDocumentServiceTests
{
    [Fact]
    public async Task AddAndDeleteAsync_NormalizePersistProtectAndAuditMetadataWithoutContent()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new BusinessPartyDocumentService(test.Db, test.Access);

        var document = await service.AddAsync(
            test.UserId,
            Request(test) with
            {
                Name = " Supplier contract ",
                Description = " Annual terms ",
                FileName = " contract.pdf ",
                ContentType = " APPLICATION/PDF "
            });

        Assert.Equal("Supplier contract", document.Name);
        Assert.Equal("Annual terms", document.Description);
        Assert.Equal("contract.pdf", document.FileName);
        Assert.Equal("application/pdf", document.ContentType);
        Assert.NotNull(document.ImmutableDocumentObjectId);
        Assert.True(await test.Db.ImmutableDocumentObjects.AnyAsync(x =>
            x.Id == document.ImmutableDocumentObjectId &&
            x.OrganisationId == test.Organisation.Id));
        var protectedError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteAsync(test.UserId, test.Organisation.Id, document.Id));
        Assert.Contains("seven-year", protectedError.Message, StringComparison.OrdinalIgnoreCase);

        document.UploadedAtUtc = DateTimeOffset.UtcNow.AddYears(-9);
        await test.Db.SaveChangesAsync();
        Assert.True(await service.DeleteAsync(test.UserId, test.Organisation.Id, document.Id));
        Assert.False(await service.DeleteAsync(
            test.UserId,
            test.Organisation.Id,
            document.Id));

        var audits = await test.Db.AuditEvents
            .AsNoTracking()
            .Where(x => x.EntityId == document.Id.ToString())
            .OrderBy(x => x.Id)
            .ToListAsync();
        Assert.Equal(
            ["BusinessPartyDocumentAdded", "BusinessPartyDocumentDeleted"],
            audits.Select(x => x.EventType));
        Assert.All(audits, audit =>
        {
            Assert.Equal(nameof(BusinessPartyDocument), audit.EntityType);
            Assert.Equal(test.UserId, audit.UserId);
            Assert.DoesNotContain("Content\"", audit.JsonData, StringComparison.Ordinal);
            using var evidence = JsonDocument.Parse(audit.JsonData);
            Assert.Equal(test.Supplier.Name, evidence.RootElement.GetProperty("BusinessPartyName").GetString());
            Assert.Equal(4, evidence.RootElement.GetProperty("OriginalSize").GetInt64());
            Assert.Equal(4, evidence.RootElement.GetProperty("StoredSize").GetInt64());
        });
    }

    [Fact]
    public async Task InvalidUploads_CreateNoDocumentOrAudit()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new BusinessPartyDocumentService(test.Db, test.Access);
        var valid = Request(test);
        var invalidRequests = new[]
        {
            valid with { Type = (BusinessPartyDocumentType)999 },
            valid with { Name = " " },
            valid with { Name = new string('N', 201) },
            valid with { Description = new string('D', 501) },
            valid with { FileName = "folder/contract.pdf" },
            valid with { ContentType = "application/octet-stream" },
            valid with { OriginalSize = 0 },
            valid with { OriginalSize = 5 },
            valid with { Content = [] },
            valid with { IsCompressed = true },
            valid with { OriginalSize = BusinessPartyDocumentService.MaximumDocumentBytes + 1L }
        };

        foreach (var request in invalidRequests)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AddAsync(test.UserId, request));
        }

        Assert.Empty(await test.Db.BusinessPartyDocuments.AsNoTracking().ToListAsync());
        Assert.Empty(await test.Db.AuditEvents.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task DeleteAsync_DoesNotApplyFijiRetentionRuleToAnotherJurisdiction()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.CountryCode = "NZ";
        await test.Db.SaveChangesAsync();
        var service = new BusinessPartyDocumentService(test.Db, test.Access);
        var document = await service.AddAsync(test.UserId, Request(test));

        Assert.True(await service.DeleteAsync(
            test.UserId,
            test.Organisation.Id,
            document.Id));
    }

    [Fact]
    public async Task UnauthorizedAndCrossTenantOperations_CreateNoAuditNoise()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new BusinessPartyDocumentService(test.Db, test.Access);
        var document = await service.AddAsync(test.UserId, Request(test));
        var otherOrganisation = new Organisation
        {
            LegalName = "Other Organisation Limited",
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
        var initialAuditCount = await test.Db.AuditEvents.CountAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddAsync(
                test.UserId,
                Request(test) with { OrganisationId = otherOrganisation.Id }));
        Assert.False(await service.DeleteAsync(
            test.UserId,
            otherOrganisation.Id,
            document.Id));
        await test.Db.OrganisationMemberships
            .Where(x =>
                x.UserId == test.UserId &&
                x.OrganisationId == test.Organisation.Id)
            .ExecuteUpdateAsync(update =>
                update.SetProperty(x => x.Role, OrganisationRole.ReadOnly));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.AddAsync(test.UserId, Request(test)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.DeleteAsync(test.UserId, test.Organisation.Id, document.Id));

        Assert.True(await test.Db.BusinessPartyDocuments.AnyAsync(x => x.Id == document.Id));
        Assert.Equal(initialAuditCount, await test.Db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task GetForPartyAsync_RequiresOrganisationAccessAndScopesParty()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new BusinessPartyDocumentService(test.Db, test.Access);
        var document = await service.AddAsync(test.UserId, Request(test));

        Assert.Equal(
            document.Id,
            (await service.GetForPartyAsync(
                test.UserId,
                test.Organisation.Id,
                test.Supplier.Id)).Single().Id);
        Assert.Empty(await service.GetForPartyAsync(
            test.UserId,
            test.Organisation.Id,
            test.Customer.Id));

        await test.Db.OrganisationMemberships
            .Where(x =>
                x.UserId == test.UserId &&
                x.OrganisationId == test.Organisation.Id)
            .ExecuteUpdateAsync(update =>
                update.SetProperty(x => x.Role, OrganisationRole.ReadOnly));
        var downloaded = await service.GetAsync(
            test.UserId,
            test.Organisation.Id,
            test.Supplier.Id,
            document.Id);
        Assert.NotNull(downloaded);
        Assert.Equal(document.Id, downloaded.Id);
        Assert.Equal([1, 2, 3, 4], downloaded.Content);
        await service.RecordExportAsync(
            test.UserId,
            test.Organisation.Id,
            downloaded);
        Assert.True(await test.Db.AuditEvents.AnyAsync(x =>
            x.EntityId == document.Id.ToString() &&
            x.EventType == "BusinessPartyDocumentExported"));
        Assert.Null(await service.GetAsync(
            test.UserId,
            test.Organisation.Id,
            test.Customer.Id,
            document.Id));
        Assert.Null(await service.GetAsync(
            "not-a-member",
            test.Organisation.Id,
            test.Supplier.Id,
            document.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetForPartyAsync(
                test.UserId,
                Guid.NewGuid(),
                test.Supplier.Id));
    }

    private static BusinessPartyDocumentUploadRequest Request(AccountingTestDatabase test) =>
        new(
            test.Organisation.Id,
            test.Supplier.Id,
            BusinessPartyDocumentType.Contract,
            "Supplier contract",
            null,
            "contract.pdf",
            "application/pdf",
            [1, 2, 3, 4],
            4,
            false,
            new DateOnly(2027, 8, 23));
}
