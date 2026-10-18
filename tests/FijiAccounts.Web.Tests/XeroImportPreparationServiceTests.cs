using System.Text;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class XeroImportPreparationServiceTests
{
    [Fact]
    public async Task PreparationIndexesLatestSnapshotsAndDoesNotDuplicateSourceRecords()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = await SetupAsync(test);
        await AddSnapshotAsync(test, "accounts", "Accounts", """
            {"Accounts":[{"AccountID":"acc-1","Code":"7100","Name":"Travel","Type":"EXPENSE","Status":"ACTIVE"}]}
            """);

        var first = await service.PrepareAsync(test.UserId, test.Organisation.Id);
        var second = await service.PrepareAsync(test.UserId, test.Organisation.Id);

        Assert.Equal(1, first.SourceRecordCount);
        Assert.Equal(1, second.SourceRecordCount);
        Assert.Equal(1, await test.Db.XeroMigrationRecords.CountAsync());
        Assert.True(await test.Db.XeroMigrationIssues.AnyAsync(x =>
            !x.IsResolved && x.Code == "JOURNALS_MISSING" && x.Severity == XeroMigrationIssueSeverity.Warning));
    }

    [Fact]
    public async Task MasterImportCreatesUsableRecordsAndLinksSourceIdentity()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = await SetupAsync(test);
        await AddSnapshotAsync(test, "accounts", "Accounts", """
            {"Accounts":[{"AccountID":"acc-1","Code":"7100","Name":"Travel","Type":"EXPENSE","Status":"ACTIVE"}]}
            """);
        await AddSnapshotAsync(test, "contacts", "Contacts", """
            {"Contacts":[{"ContactID":"contact-1","Name":"Migration Customer","EmailAddress":"accounts@example.test","IsCustomer":true,"IsSupplier":false,"ContactStatus":"ACTIVE"}]}
            """);
        await AddSnapshotAsync(test, "currencies", "Currencies", """
            {"Currencies":[{"Code":"AUD","Description":"Australian Dollar"}]}
            """);
        await AddSnapshotAsync(test, "items", "Items", """
            {"Items":[{"ItemID":"item-1","Code":"CONSULT","Name":"Consulting","IsSold":true,"IsPurchased":false,"IsTrackedAsInventory":false,"SalesDetails":{"UnitPrice":250}}]}
            """);
        await service.PrepareAsync(test.UserId, test.Organisation.Id);

        var imported = await service.ImportMasterDataAsync(test.UserId, test.Organisation.Id);

        Assert.Equal(4, imported);
        Assert.True(await test.Db.LedgerAccounts.AnyAsync(x => x.Code == "7100" && x.Name == "Travel"));
        Assert.True(await test.Db.BusinessParties.AnyAsync(x => x.Name == "Migration Customer" && x.Email == "accounts@example.test"));
        Assert.True(await test.Db.OrganisationCurrencies.AnyAsync(x => x.Code == "AUD"));
        Assert.True(await test.Db.ProductItems.AnyAsync(x => x.Code == "CONSULT" && x.SalePrice == 250m));
        Assert.Equal(4, await test.Db.XeroMigrationRecords.CountAsync(x =>
            x.Status == XeroMigrationRecordStatus.Imported && x.TargetEntityId != null));

        Assert.Equal(0, await service.ImportMasterDataAsync(test.UserId, test.Organisation.Id));
    }

    [Fact]
    public async Task TaxRatesAreAutomaticallyMappedButUnknownRatesBlockCutover()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = await SetupAsync(test);
        await AddSnapshotAsync(test, "tax-rates", "TaxRates", """
            {"TaxRates":[
              {"TaxType":"OUTPUT2","Name":"GST on Income"},
              {"TaxType":"SPECIAL","Name":"Historic custom tax"}
            ]}
            """);

        await service.PrepareAsync(test.UserId, test.Organisation.Id);

        var standard = await test.Db.XeroMigrationMappings.SingleAsync(x => x.SourceKey == "OUTPUT2");
        var unknown = await test.Db.XeroMigrationMappings.SingleAsync(x => x.SourceKey == "SPECIAL");
        Assert.Equal(XeroMigrationMappingStatus.Confirmed, standard.Status);
        Assert.Equal("Standard", standard.TargetDisplayName);
        Assert.Equal(XeroMigrationMappingStatus.Suggested, unknown.Status);
        Assert.True(await test.Db.XeroMigrationIssues.AnyAsync(x =>
            !x.IsResolved && x.Code == "MAPPING_REQUIRED" && x.Message.Contains("Historic custom tax")));
    }

    [Fact]
    public async Task FinancialRecordsRemainBlockingUntilTheyAreConverted()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = await SetupAsync(test);
        await AddSnapshotAsync(test, "accounts", "Accounts", """
            {"Accounts":[{"AccountID":"a-1","Code":"200","Name":"Sales","Type":"REVENUE","Status":"ACTIVE"}]}
            """);
        await AddSnapshotAsync(test, "manual-journals", "ManualJournals", """
            {"ManualJournals":[{"ManualJournalID":"j-1","Narration":"Opening balances","Date":"/Date(1798761600000)/"}]}
            """);
        await AddSnapshotAsync(test, "sales-invoices", "Invoices", """
            {"Invoices":[{"InvoiceID":"i-1","InvoiceNumber":"INV-1","Date":"/Date(1798761600000)/"}]}
            """);

        await service.PrepareAsync(test.UserId, test.Organisation.Id);

        Assert.True(await test.Db.XeroMigrationIssues.AnyAsync(x =>
            !x.IsResolved && x.Code == "LIVE_IMPORT_PENDING" &&
            x.Message.Contains("sales invoices")));
    }

    private static async Task<XeroImportPreparationService> SetupAsync(AccountingTestDatabase test)
    {
        test.Organisation.CountryCode = "NZ";
        test.Organisation.BaseCurrency = "NZD";
        var migration = new XeroMigration
        {
            OrganisationId = test.Organisation.Id,
            SourceOrganisationName = "Migration Test",
            CutoverDate = new DateOnly(2026, 11, 1),
            CreatedByUserId = test.UserId,
            UpdatedByUserId = test.UserId
        };
        test.Db.XeroMigrations.Add(migration);
        await test.Db.SaveChangesAsync();
        return new XeroImportPreparationService(test.Db, test.Access, new DatabaseImmutableDocumentStore(test.Db));
    }

    private static async Task AddSnapshotAsync(
        AccountingTestDatabase test, string dataset, string root, string pageJson)
    {
        var migration = await test.Db.XeroMigrations.SingleAsync();
        var wrapped = $$"""
            {"dataset":"{{dataset}}","source":"Xero API","retrievedAt":"2026-10-16T00:00:00Z","recordCount":1,"pages":[{{pageJson}}]}
            """;
        var bytes = Encoding.UTF8.GetBytes(wrapped);
        var store = new DatabaseImmutableDocumentStore(test.Db);
        var retained = store.Stage(test.Organisation.Id, test.UserId, bytes);
        test.Db.XeroApiSnapshots.Add(new XeroApiSnapshot
        {
            OrganisationId = test.Organisation.Id,
            XeroMigrationId = migration.Id,
            DatasetKey = dataset,
            DisplayName = root,
            Endpoint = root,
            RecordCount = 1,
            PageCount = 1,
            Size = bytes.Length,
            Sha256 = retained.Sha256,
            ImmutableDocumentObjectId = retained.Id,
            RetrievedByUserId = test.UserId
        });
        await test.Db.SaveChangesAsync();
    }
}
