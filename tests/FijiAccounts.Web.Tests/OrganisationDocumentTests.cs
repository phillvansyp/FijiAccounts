using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;
namespace FijiAccounts.Web.Tests;

public sealed class OrganisationDocumentTests
{
    [Fact]
    public async Task UploadPreservesContentAndRecordsAudit()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new OrganisationDocumentService(test.Db, new DatabaseImmutableDocumentStore(test.Db));
        var bytes = "%PDF-1.7\nregistration evidence"u8.ToArray();
        var document = await service.AddAsync(test.UserId, test.Organisation.Id, "VAT registration", "vat.pdf", bytes);
        Assert.Equal(bytes, await service.ReadAsync(test.UserId, test.Organisation.Id, document.Id));
        Assert.Single(await service.ListAsync(test.UserId, test.Organisation.Id));
        Assert.True(await test.Db.AuditEvents.AnyAsync(x => x.EntityId == document.Id.ToString() && x.EventType == "OrganisationDocumentAdded"));
    }

    [Fact]
    public async Task OtherUserAndOtherOrganisationCannotReadOrUpload()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new OrganisationDocumentService(test.Db, new DatabaseImmutableDocumentStore(test.Db));
        var document = await service.AddAsync(test.UserId, test.Organisation.Id, "Company registration", "company.pdf", "%PDF-evidence"u8.ToArray());
        Assert.Null(await service.ReadAsync("other-user", test.Organisation.Id, document.Id));
        Assert.Null(await service.ReadAsync(test.UserId, Guid.NewGuid(), document.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.AddAsync("other-user", test.Organisation.Id, "Other", "file.pdf", "%PDF-evidence"u8.ToArray()));
    }

    [Theory]
    [InlineData("file.pdf", "not a PDF")]
    [InlineData("file.html", "%PDF-evidence")]
    [InlineData("../file.pdf", "%PDF-evidence")]
    public async Task InvalidFilesAreRejected(string name, string content)
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new OrganisationDocumentService(test.Db, new DatabaseImmutableDocumentStore(test.Db));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAsync(test.UserId, test.Organisation.Id, "Other", name, System.Text.Encoding.UTF8.GetBytes(content)));
        Assert.Empty(await service.ListAsync(test.UserId, test.Organisation.Id));
    }
}
