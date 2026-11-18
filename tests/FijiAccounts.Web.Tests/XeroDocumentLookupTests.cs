using FijiAccounts.Web.Data;
using FijiAccounts.Web.Data.Migrations;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace FijiAccounts.Web.Tests;

public sealed class XeroDocumentLookupTests
{
    [Fact]
    public async Task BillRegisterReusesRowsAndRefreshesAfterMirrorOrConnectionChanges()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var db = test.Db;
        var connection = new XeroConnection { OrganisationId=test.Organisation.Id, ProtectedAccessToken="", ProtectedRefreshToken="",
            GrantedScopes="", ConnectedByUserId=test.UserId, UpdatedByUserId=test.UserId };
        var row = new XeroMirrorRecord { OrganisationId=test.Organisation.Id, Connection=connection,
            DatasetKey="bills", ExternalId="bill", DisplayName="B-1", SourceHash="seed",
            SourceJson="""{"Date":"2026-03-01","DueDate":"2026-04-01","Status":"AUTHORISED","AmountDue":10}""" };
        db.Add(row);
        await db.SaveChangesAsync();
        var migration = new TrackBillDirectoryChanges();
        foreach (var op in migration.UpOperations.Cast<SqlOperation>()) await db.Database.ExecuteSqlRawAsync(op.Sql);
        using var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        Task<IReadOnlyList<XeroSalesInvoiceProjection>> Read() => XeroBillDirectory.ReadAsync(db,cache,test.Organisation.Id);
        var first = await Read();
        Assert.Equal(10m,Assert.Single(first).AmountDue);
        Assert.Same(first,await Read());
        row.SourceJson=row.SourceJson.Replace("10","7");
        await db.SaveChangesAsync();
        Assert.Equal(7m,Assert.Single(await Read()).AmountDue);
        connection.IsActive=false;
        await db.SaveChangesAsync();
        Assert.Empty(await Read());
        connection.IsActive=true;
        row.IsArchived=true;
        await db.SaveChangesAsync();
        Assert.Empty(await Read());
        row.IsArchived=false;
        await db.SaveChangesAsync();
        Assert.Single(await Read());
        db.Remove(row);
        await db.SaveChangesAsync();
        Assert.Empty(await Read());
        foreach (var op in migration.DownOperations.Cast<SqlOperation>()) await db.Database.ExecuteSqlRawAsync(op.Sql);
    }

    [Fact]
    public async Task IndexedReadsKeepOpenAmountsAndLegacyContactCandidatesAcrossUpdates()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var db = test.Db;
        var org = test.Organisation.Id;
        var connection = new XeroConnection { OrganisationId=org, ProtectedAccessToken="", ProtectedRefreshToken="",
            GrantedScopes="", ConnectedByUserId=test.UserId, UpdatedByUserId=test.UserId };
        var contact = Guid.NewGuid().ToString();
        XeroMirrorRecord Row(string number, string json) => new() {
            OrganisationId=org, Connection=connection, ExternalId=number, DisplayName=number,
            DatasetKey="sales-invoices", SourceHash="test", SourceJson=json
        };
        var paid = Row("paid", """{"Date":"2026-03-01","DueDate":"2026-04-01","Status":"PAID","AmountDue":0}""");
        var open = Row("open", """{"Date":"2026-03-01","DueDate":"2026-04-01","Status":"PAID","Status":"authorised","AmountDue":"1,234.56","Contact":{"ContactID":"test-contact"}}""".Replace("test-contact",contact));
        var broken = Row("legacy", "invalid " + contact);
        var other = Row("other", """{"Contact":{"ContactID":"different"}}""");
        db.AddRange(paid,open,broken,other);
        await db.SaveChangesAsync();
        var migration = new IndexOpenXeroDocuments();
        foreach(var op in migration.UpOperations.Cast<SqlOperation>()) await db.Database.ExecuteSqlRawAsync(op.Sql);
        var initial = Assert.Single(await XeroSalesInvoiceProjectionParser.ReadCurrentAsync(db,org,openOnly:true));
        Assert.Equal(1234.56m,initial.AmountDue);
        Assert.Equal(open.Id,initial.RecordId);
        async Task<HashSet<Guid>> ForContact(Guid organisation)
        {
            var ids = new HashSet<Guid>();
            await foreach(var row in XeroContactDocumentQuery.Read(db,organisation,connection.Id,contact))
                ids.Add(row.Id);
            return ids;
        }
        Assert.True((await ForContact(org)).SetEquals([open.Id,broken.Id]));
        Assert.Empty(await ForContact(Guid.NewGuid()));
        open.IsArchived=true;
        paid.SourceJson=paid.SourceJson.Replace("PAID","AUTHORISED").Replace("\"AmountDue\":0","\"AmountDue\":10");
        await db.SaveChangesAsync();
        Assert.Equal(paid.Id,Assert.Single(await XeroSalesInvoiceProjectionParser.ReadCurrentAsync(db,org,openOnly:true)).RecordId);
        Assert.True((await ForContact(org)).SetEquals([broken.Id]));
        foreach(var op in migration.DownOperations.Cast<SqlOperation>()) await db.Database.ExecuteSqlRawAsync(op.Sql);
        Assert.Equal(paid.Id,Assert.Single(await XeroSalesInvoiceProjectionParser.ReadCurrentAsync(db,org,openOnly:true)).RecordId);
    }
}
