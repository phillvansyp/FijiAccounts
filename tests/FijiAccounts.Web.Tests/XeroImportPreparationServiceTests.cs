using System.Text;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class XeroImportPreparationServiceTests
{
    [Fact]
    public async Task TrackingCategoryChoosesAnAccountIslandDimensionInsteadOfOneBranch()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = await SetupAsync(test);
        var migration = await test.Db.XeroMigrations.SingleAsync();
        var mapping = new XeroMigrationMapping
        {
            OrganisationId = test.Organisation.Id,
            XeroMigrationId = migration.Id,
            Kind = XeroMigrationMappingKind.TrackingCategory,
            SourceKey = "xero-category-id",
            SourceName = "Branch",
            TargetEntityType = "Branch or division",
            Status = XeroMigrationMappingStatus.Suggested,
            UpdatedByUserId = test.UserId
        };
        test.Db.XeroMigrationMappings.Add(mapping);
        await test.Db.SaveChangesAsync();

        var targets = await service.GetMappingTargetsAsync(
            test.UserId, test.Organisation.Id, XeroMigrationMappingKind.TrackingCategory);

        Assert.Collection(targets,
            branch => Assert.Equal("Use Account Island branches", branch.Label),
            division => Assert.Equal("Use Account Island divisions", division.Label));
        Assert.All(targets, target => Assert.Null(target.Id));

        var branchTarget = targets[0];
        await service.ConfirmMappingAsync(test.UserId, test.Organisation.Id, mapping.Id,
            branchTarget.Id, branchTarget.Label, branchTarget.EntityType, false);

        var saved = await test.Db.XeroMigrationMappings.SingleAsync(x => x.Id == mapping.Id);
        Assert.Equal(XeroMigrationMappingStatus.Confirmed, saved.Status);
        Assert.Equal(nameof(Branch), saved.TargetEntityType);
        Assert.Null(saved.TargetEntityId);
    }

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
    public async Task PreparationHandlesPreviouslyRetainedIdentifiersThatDifferOnlyByCase()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = await SetupAsync(test);
        await AddSnapshotAsync(test, "accounts", "Accounts", """
            {"Accounts":[{"AccountID":"acc-1","Code":"7100","Name":"Travel","Type":"EXPENSE","Status":"ACTIVE"}]}
            """);
        var migration = await test.Db.XeroMigrations.SingleAsync();
        var snapshot = await test.Db.XeroApiSnapshots.SingleAsync();
        test.Db.XeroMigrationRecords.AddRange(
            new XeroMigrationRecord
            {
                OrganisationId = test.Organisation.Id, XeroMigrationId = migration.Id,
                XeroApiSnapshotId = snapshot.Id, DatasetKey = "accounts", ExternalId = "ACC-1",
                DisplayName = "Older retained account", SourceHash = new string('A', 64),
                Status = XeroMigrationRecordStatus.Ready, PreparedByUserId = test.UserId
            },
            new XeroMigrationRecord
            {
                OrganisationId = test.Organisation.Id, XeroMigrationId = migration.Id,
                XeroApiSnapshotId = snapshot.Id, DatasetKey = "accounts", ExternalId = "acc-1",
                DisplayName = "Current account", SourceHash = new string('B', 64),
                Status = XeroMigrationRecordStatus.Ready, PreparedByUserId = test.UserId
            });
        test.Db.XeroMigrationMappings.AddRange(
            new XeroMigrationMapping
            {
                OrganisationId = test.Organisation.Id, XeroMigrationId = migration.Id,
                Kind = XeroMigrationMappingKind.Account, SourceKey = "DUP", SourceName = "Upper",
                Status = XeroMigrationMappingStatus.Suggested, UpdatedByUserId = test.UserId
            },
            new XeroMigrationMapping
            {
                OrganisationId = test.Organisation.Id, XeroMigrationId = migration.Id,
                Kind = XeroMigrationMappingKind.Account, SourceKey = "dup", SourceName = "Lower",
                Status = XeroMigrationMappingStatus.Suggested, UpdatedByUserId = test.UserId
            });
        await test.Db.SaveChangesAsync();

        var result = await service.PrepareAsync(test.UserId, test.Organisation.Id);

        Assert.Equal(2, result.SourceRecordCount);
        Assert.Equal(XeroMigrationRecordStatus.Skipped,
            (await test.Db.XeroMigrationRecords.SingleAsync(x => x.ExternalId == "ACC-1")).Status);
        Assert.Equal(2, await test.Db.XeroMigrationMappings.CountAsync(x =>
            x.SourceKey == "DUP" || x.SourceKey == "dup"));
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
    public async Task MasterImportArchivesUnusableXeroSystemRecordsInsteadOfBlockingCutover()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = await SetupAsync(test);
        await AddSnapshotAsync(test, "accounts", "Accounts", """
            {"Accounts":[
              {"AccountID":"acc-1","Code":"7100","Name":"Travel","Type":"EXPENSE","Status":"ACTIVE"},
              {"AccountID":"system-1","Code":"","Name":"","Type":"CURRLIAB","Status":"ACTIVE"}
            ]}
            """);
        await service.PrepareAsync(test.UserId, test.Organisation.Id);

        var imported = await service.ImportMasterDataAsync(test.UserId, test.Organisation.Id);

        Assert.Equal(1, imported);
        var retained = await test.Db.XeroMigrationRecords.SingleAsync(x => x.ExternalId == "system-1");
        Assert.Equal(XeroMigrationRecordStatus.Skipped, retained.Status);
        Assert.Equal("Immutable Xero history", retained.TargetEntityType);
        Assert.Contains("no usable name", retained.ReviewMessage);
        Assert.False(await test.Db.XeroMigrationRecords.AnyAsync(x =>
            new[] { "accounts", "contacts", "currencies", "items" }.Contains(x.DatasetKey) &&
            x.Status != XeroMigrationRecordStatus.Imported && x.Status != XeroMigrationRecordStatus.Skipped));
    }

    [Fact]
    public async Task BankAccountWithoutXeroCodeGetsStableAccountIslandCode()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = await SetupAsync(test);
        await AddSnapshotAsync(test, "accounts", "Accounts", """
            {"Accounts":[{"AccountID":"bank-kiwibank","Code":"","Name":"Kiwibank 00","Type":"BANK","BankAccountNumber":"38-9027-0295495-00","Status":"ACTIVE"}]}
            """);
        await service.PrepareAsync(test.UserId, test.Organisation.Id);

        Assert.Equal(1, await service.ImportMasterDataAsync(test.UserId, test.Organisation.Id));

        var account = await test.Db.LedgerAccounts.SingleAsync(x => x.Name == "Kiwibank 00");
        Assert.StartsWith("XERO-", account.Code);
        Assert.True(account.IsBankAccount);
        Assert.Equal("38-9027-0295495-00", account.BankAccountNumber);
        var record = await test.Db.XeroMigrationRecords.SingleAsync(x => x.ExternalId == "bank-kiwibank");
        Assert.Equal(XeroMigrationRecordStatus.Imported, record.Status);
        Assert.Equal(account.Id, record.TargetEntityId);
    }

    [Fact]
    public async Task NewlySupportedBankAccountClearsPreviousRetentionIssue()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = await SetupAsync(test);
        await AddSnapshotAsync(test, "accounts", "Accounts", """
            {"Accounts":[{"AccountID":"bank-kiwibank","Code":"","Name":"Kiwibank 00","Type":"BANK","BankAccountNumber":"38-9027-0295495-00","Status":"ACTIVE"}]}
            """);
        await service.PrepareAsync(test.UserId, test.Organisation.Id);
        var record = await test.Db.XeroMigrationRecords.SingleAsync();
        record.Status = XeroMigrationRecordStatus.Skipped;
        record.ReviewMessage = "Retained in the Xero archive because the source account has no usable code or name.";
        await test.Db.SaveChangesAsync();

        Assert.Equal(1, await service.ImportMasterDataAsync(test.UserId, test.Organisation.Id));

        record = await test.Db.XeroMigrationRecords.SingleAsync();
        Assert.Equal(XeroMigrationRecordStatus.Imported, record.Status);
        Assert.Null(record.ReviewMessage);
        Assert.False(await test.Db.XeroMigrationIssues.AnyAsync(x =>
            !x.IsResolved && x.XeroMigrationRecordId == record.Id));
    }

    [Fact]
    public async Task CurrentAssetIsNotClassifiedAsBankAndRerunRepairsExistingClassification()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = await SetupAsync(test);
        await AddSnapshotAsync(test, "accounts", "Accounts", """
            {"Accounts":[{"AccountID":"receivables","Code":"674","Name":"Accounts receivable","Type":"CURRENT","Status":"ACTIVE"}]}
            """);
        await service.PrepareAsync(test.UserId, test.Organisation.Id);

        Assert.Equal(1, await service.ImportMasterDataAsync(test.UserId, test.Organisation.Id));
        var account = await test.Db.LedgerAccounts.SingleAsync(x => x.Code == "674");
        Assert.False(account.IsBankAccount);

        account.IsBankAccount = true;
        await test.Db.SaveChangesAsync();
        Assert.Equal(0, await service.ImportMasterDataAsync(test.UserId, test.Organisation.Id));
        Assert.False((await test.Db.LedgerAccounts.SingleAsync(x => x.Code == "674")).IsBankAccount);
    }

    [Fact]
    public async Task ActiveXeroBankAccountReactivatesMatchingAccountIslandAccount()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = await SetupAsync(test);
        var existing = new LedgerAccount
        {
            OrganisationId = test.Organisation.Id, Code = "XERO-OLD",
            Name = "Kiwibank 00", Type = FijiAccounts.Domain.Accounting.AccountType.Asset,
            IsBankAccount = true, IsActive = false
        };
        test.Db.LedgerAccounts.Add(existing);
        await test.Db.SaveChangesAsync();
        await AddSnapshotAsync(test, "accounts", "Accounts", """
            {"Accounts":[{"AccountID":"bank-kiwibank","Code":"","Name":"Kiwibank 00","Type":"BANK","BankAccountNumber":"38-9027-0295495-00","Status":"ACTIVE"}]}
            """);
        await service.PrepareAsync(test.UserId, test.Organisation.Id);

        Assert.Equal(1, await service.ImportMasterDataAsync(test.UserId, test.Organisation.Id));

        var account = await test.Db.LedgerAccounts.SingleAsync(x => x.Name == "Kiwibank 00");
        Assert.Equal(existing.Id, account.Id);
        Assert.True(account.IsActive);
        Assert.Equal("38-9027-0295495-00", account.BankAccountNumber);
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
    public async Task AssetTypesUseTheirXeroFixedAssetAccountsForAutomaticMapping()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = await SetupAsync(test);
        await AddSnapshotAsync(test, "accounts", "Accounts", """
            {"Accounts":[
              {"AccountID":"land-account","Code":"730","Name":"Land (cost)","Type":"FIXED","Status":"ACTIVE"},
              {"AccountID":"plant-account","Code":"742","Name":"Plant and equipment (cost)","Type":"FIXED","Status":"ACTIVE"}
            ]}
            """);
        await AddSnapshotAsync(test, "asset-types", "AssetTypes", """
            {"AssetTypes":[
              {"AssetTypeID":"land-type","AssetTypeName":"Land","FixedAssetAccountID":"land-account"},
              {"AssetTypeID":"plant-type","AssetTypeName":"Plant","FixedAssetAccountID":"plant-account"}
            ]}
            """);

        await service.PrepareAsync(test.UserId, test.Organisation.Id);
        await service.ImportMasterDataAsync(test.UserId, test.Organisation.Id);

        var mappings = await test.Db.XeroMigrationMappings
            .Where(x => x.Kind == XeroMigrationMappingKind.AssetType)
            .OrderBy(x => x.SourceName)
            .ToListAsync();
        Assert.Equal(2, mappings.Count);
        Assert.All(mappings, mapping => Assert.Equal(XeroMigrationMappingStatus.Confirmed, mapping.Status));
        Assert.Equal("730 · Land (cost)", mappings[0].TargetDisplayName);
        Assert.Equal("Xero assigned this asset type to 730 · Land (cost).", mappings[0].Notes);
        Assert.Equal("742 · Plant and equipment (cost)", mappings[1].TargetDisplayName);
    }

    [Fact]
    public async Task FinancialRecordsShowTheCutoverPlanWithoutBlockingApproval()
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
            x.Severity == XeroMigrationIssueSeverity.Information &&
            x.Message.Contains("open items") && x.Message.Contains("immutable Xero history")));
    }

    [Fact]
    public async Task AttachmentSyncFailureRemainsBlockingEvenWhenAnOlderPackageExists()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = await SetupAsync(test);
        await AddSnapshotAsync(test, "accounts", "Accounts", """
            {"Accounts":[{"AccountID":"a-1","Code":"200","Name":"Sales","Type":"REVENUE","Status":"ACTIVE"}]}
            """);
        test.Db.XeroConnections.Add(new XeroConnection
        {
            OrganisationId = test.Organisation.Id,
            XeroTenantId = Guid.NewGuid(),
            ProtectedAccessToken = "protected",
            ProtectedRefreshToken = "protected",
            GrantedScopes = "accounting.attachments.read",
            ConnectedByUserId = test.UserId,
            UpdatedByUserId = test.UserId,
            LastSyncError = "Document attachments: Xero returned 503 Service Unavailable."
        });
        var migration = await test.Db.XeroMigrations.SingleAsync();
        var retained = new DatabaseImmutableDocumentStore(test.Db).Stage(
            test.Organisation.Id, test.UserId, Encoding.UTF8.GetBytes("older attachment package"));
        test.Db.XeroMigrationFiles.Add(new XeroMigrationFile
        {
            OrganisationId = test.Organisation.Id,
            XeroMigrationId = migration.Id,
            Category = "Document attachments",
            FileName = "older-attachments.zip",
            ContentType = "application/zip",
            Size = retained.ContentLength,
            Sha256 = retained.Sha256,
            ImmutableDocumentObjectId = retained.Id,
            UploadedByUserId = test.UserId
        });
        await test.Db.SaveChangesAsync();

        await service.PrepareAsync(test.UserId, test.Organisation.Id);

        Assert.True(await test.Db.XeroMigrationIssues.AnyAsync(x =>
            !x.IsResolved && x.Code == "ATTACHMENT_SYNC_FAILED" &&
            x.Severity == XeroMigrationIssueSeverity.Blocking));
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
