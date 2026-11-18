using FijiAccounts.Web.Data;
using FijiAccounts.Web.Data.Migrations;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Caching.Memory;

namespace FijiAccounts.Web.Tests;

public sealed class XeroSalesDirectoryTests
{
    [Fact]
    public async Task BackfillAndChangesMatchInvoiceDetailAndInvalidateCachedResults()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var db = test.Db;
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var connection = new XeroConnection
        {
            OrganisationId = test.Organisation.Id, ProtectedAccessToken = "", ProtectedRefreshToken = "",
            GrantedScopes = "", ConnectedByUserId = test.UserId, UpdatedByUserId = test.UserId
        };
        var record = new XeroMirrorRecord
        {
            OrganisationId = test.Organisation.Id, Connection = connection, DatasetKey = "sales-invoices",
            ExternalId = "123", SourceHash = "abc", DisplayName = "INV-123",
            SourceJson = """{"Date":"/Date(1711929600000+0000)/","DueDate":"2024-05-01","Status":"AUTHORISED","Total":12345.67890123456789,"AmountDue":"120.25","CurrencyRate":1.010101,"CurrencyCode":"USD","Reference":"PO-42","Contact":{"ContactID":"fa5d4534-945b-422e-9e98-78571ea8a416","Name":"Māia"},"LineItems":[{"Description":"Full invoice detail"}]}"""
        };
        db.XeroMirrorRecords.Add(record);
        await db.SaveChangesAsync();
        foreach (var op in new IndexXeroSalesDirectory().UpOperations.Cast<SqlOperation>())
            await db.Database.ExecuteSqlRawAsync(op.Sql);

        Task<IReadOnlyList<XeroSalesInvoiceProjection>> Read() => XeroSalesDirectory.ReadAsync(db, cache, test.Organisation.Id);
        var initial = await Read();
        var invoice = Assert.Single(initial);
        Assert.True(XeroSalesInvoiceProjectionParser.TryParse(record, out var expected, includeLines: false));
        Assert.Equal(expected with { Lines = invoice.Lines }, invoice);
        Assert.Same(initial, await Read());
        Assert.Empty(await XeroSalesDirectory.ReadAsync(db, cache, Guid.NewGuid()));
        record.LastSeenAt = record.LastSeenAt.AddMinutes(5);
        await db.SaveChangesAsync();
        Assert.Same(initial, await Read());

        record.SourceJson = record.SourceJson.Replace("120.25", "87.50");
        await db.SaveChangesAsync();
        Assert.Equal(87.50m, Assert.Single(await Read()).AmountDue);
        record.IsArchived = true;
        await db.SaveChangesAsync();
        Assert.Empty(await Read());
        record.IsArchived = false;
        await db.SaveChangesAsync();
        Assert.Single(await Read());
        connection.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Empty(await Read());
        connection.IsActive = true;
        db.XeroMirrorRecords.Remove(record);
        await db.SaveChangesAsync();
        Assert.Empty(await Read());
        db.XeroMirrorRecords.Add(record);
        await db.SaveChangesAsync();
        Assert.Equal("INV-123", Assert.Single(await Read()).InvoiceNumber);
    }
}
