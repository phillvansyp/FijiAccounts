using System.Text;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class XeroCutoverImportServiceTests
{
    [Fact]
    public async Task BalancedCsvPostsOpeningJournalOnce()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.CountryCode = "NZ";
        test.Organisation.BaseCurrency = "NZD";
        var migration = new XeroMigration
        {
            OrganisationId = test.Organisation.Id,
            SourceOrganisationName = "Xero Test Limited",
            CutoverDate = new DateOnly(2026, 11, 1),
            XeroEntryStopped = true,
            TrialBalanceReconciled = true,
            ReceivablesReconciled = true,
            PayablesReconciled = true,
            BankReconciled = true,
            FixedAssetsReconciled = true,
            GstReconciled = true,
            ManagementApproved = true,
            CreatedByUserId = test.UserId,
            UpdatedByUserId = test.UserId
        };
        test.Db.XeroMigrations.Add(migration);
        var store = new DatabaseImmutableDocumentStore(test.Db);
        foreach (var category in XeroMigrationService.RequiredCategories)
        {
            var content = category == "Trial balance"
                ? Encoding.UTF8.GetBytes("Account Code,Account,Debit,Credit\r\n1000,Bank,100.00,\r\n3000,Owner's Equity,,100.00\r\n")
                : Encoding.UTF8.GetBytes("retained source export\r\n");
            var retained = store.Stage(test.Organisation.Id, test.UserId, content);
            test.Db.XeroMigrationFiles.Add(new XeroMigrationFile
            {
                OrganisationId = test.Organisation.Id,
                XeroMigrationId = migration.Id,
                Category = category,
                FileName = category == "Trial balance" ? "trial-balance.csv" : category.Replace(' ', '-') + ".csv",
                ContentType = "text/csv",
                Size = content.Length,
                Sha256 = retained.Sha256,
                ImmutableDocumentObjectId = retained.Id,
                UploadedByUserId = test.UserId
            });
        }
        await test.Db.SaveChangesAsync();

        var preparation = new XeroImportPreparationService(test.Db, test.Access, store);
        var service = new XeroCutoverImportService(test.Db, test.Access, store, test.Posting, preparation);
        var first = await service.BuildAsync(test.UserId, test.Organisation.Id);
        var second = await service.BuildAsync(test.UserId, test.Organisation.Id);

        Assert.Equal(first.OpeningJournalId, second.OpeningJournalId);
        Assert.Equal(2, first.OpeningBalanceLines);
        Assert.Equal(1, await test.Db.PostedJournals.CountAsync(x => x.Reference.StartsWith("XERO-OPEN-")));
        Assert.Equal(100m, await test.Db.PostedJournalLines.Where(x => x.PostedJournalId == first.OpeningJournalId).SumAsync(x => x.Debit));
        Assert.Equal(100m, await test.Db.PostedJournalLines.Where(x => x.PostedJournalId == first.OpeningJournalId).SumAsync(x => x.Credit));
    }

    [Fact]
    public async Task UnbalancedCsvIsRejectedBeforePosting()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.CountryCode = "NZ";
        var migration = new XeroMigration
        {
            OrganisationId = test.Organisation.Id, SourceOrganisationName = "Xero Test Limited",
            CutoverDate = new DateOnly(2026, 11, 1), XeroEntryStopped = true,
            TrialBalanceReconciled = true, ReceivablesReconciled = true, PayablesReconciled = true,
            BankReconciled = true, FixedAssetsReconciled = true, GstReconciled = true,
            ManagementApproved = true, CreatedByUserId = test.UserId, UpdatedByUserId = test.UserId
        };
        test.Db.XeroMigrations.Add(migration);
        var store = new DatabaseImmutableDocumentStore(test.Db);
        foreach (var category in XeroMigrationService.RequiredCategories)
        {
            var content = category == "Trial balance"
                ? Encoding.UTF8.GetBytes("Account Code,Account,Debit,Credit\n1000,Bank,100.00,\n3000,Owner's Equity,,99.00\n")
                : Encoding.UTF8.GetBytes("retained\n");
            var retained = store.Stage(test.Organisation.Id, test.UserId, content);
            test.Db.XeroMigrationFiles.Add(new XeroMigrationFile
            {
                OrganisationId = test.Organisation.Id, XeroMigrationId = migration.Id, Category = category,
                FileName = category == "Trial balance" ? "trial-balance.csv" : category.Replace(' ', '-') + ".csv",
                ContentType = "text/csv", Size = content.Length, Sha256 = retained.Sha256,
                ImmutableDocumentObjectId = retained.Id, UploadedByUserId = test.UserId
            });
        }
        await test.Db.SaveChangesAsync();
        var preparation = new XeroImportPreparationService(test.Db, test.Access, store);
        var service = new XeroCutoverImportService(test.Db, test.Access, store, test.Posting, preparation);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.BuildAsync(test.UserId, test.Organisation.Id));
        Assert.Contains("not balanced", error.Message);
        Assert.Empty(await test.Db.PostedJournals.ToListAsync());
    }

    [Fact]
    public async Task OpenSupplierCreditIsImportedOnceAndVoidedCreditIsIgnored()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.CountryCode = "NZ";
        test.Organisation.BaseCurrency = "NZD";
        var migration = ApprovedMigration(test, new DateOnly(2026, 10, 18));
        test.Db.XeroMigrations.Add(migration);
        var store = new DatabaseImmutableDocumentStore(test.Db);
        await AddRequiredExportsAsync(test, migration, store,
            "Account Code,Account,Debit,Credit\r\n1000,Bank,100.00,\r\n2000,Accounts Payable,,42.85\r\n3000,Owner's Equity,,57.15\r\n");

        var snapshotJson = """
            {"dataset":"supplier-credits","pages":[{"CreditNotes":[
              {"CreditNoteID":"open-credit","CreditNoteNumber":"741772019 November 25","Date":"2026-10-17","Status":"AUTHORISED","CurrencyCode":"NZD","CurrencyRate":1,"SubTotal":37.26,"TotalTax":5.59,"Total":42.85,"RemainingCredit":42.85,"Contact":{"ContactID":"spark","Name":"Spark"}},
              {"CreditNoteID":"void-credit","CreditNoteNumber":"741772019/Jan25Credit","Date":"2026-01-31","Status":"VOIDED","CurrencyCode":"NZD","CurrencyRate":1,"SubTotal":91.00,"TotalTax":13.65,"Total":104.65,"RemainingCredit":104.65,"Contact":{"ContactID":"spark","Name":"Spark"}}
            ]}]}
            """;
        var bytes = Encoding.UTF8.GetBytes(snapshotJson);
        var retained = store.Stage(test.Organisation.Id, test.UserId, bytes);
        var snapshot = new XeroApiSnapshot
        {
            OrganisationId = test.Organisation.Id, XeroMigrationId = migration.Id,
            DatasetKey = "supplier-credits", DisplayName = "Supplier credits", Endpoint = "CreditNotes",
            RecordCount = 2, PageCount = 1, Size = bytes.Length, Sha256 = retained.Sha256,
            ImmutableDocumentObjectId = retained.Id, RetrievedByUserId = test.UserId
        };
        test.Db.XeroApiSnapshots.Add(snapshot);
        test.Db.XeroMigrationRecords.Add(new XeroMigrationRecord
        {
            OrganisationId = test.Organisation.Id, XeroMigrationId = migration.Id,
            XeroApiSnapshotId = snapshot.Id, DatasetKey = "supplier-credits", ExternalId = "open-credit",
            DisplayName = "741772019 November 25", SourceHash = new string('a', 64),
            Status = XeroMigrationRecordStatus.Ready, PreparedByUserId = test.UserId
        });
        test.Db.XeroMigrationMappings.Add(new XeroMigrationMapping
        {
            OrganisationId = test.Organisation.Id, XeroMigrationId = migration.Id,
            Kind = XeroMigrationMappingKind.Contact, SourceKey = "spark", SourceName = "Spark",
            TargetEntityType = nameof(BusinessParty), TargetEntityId = test.Supplier.Id,
            TargetDisplayName = test.Supplier.Name, Status = XeroMigrationMappingStatus.Confirmed,
            UpdatedByUserId = test.UserId
        });
        await test.Db.SaveChangesAsync();

        var preparation = new XeroImportPreparationService(test.Db, test.Access, store);
        var service = new XeroCutoverImportService(test.Db, test.Access, store, test.Posting, preparation);
        var first = await service.BuildAsync(test.UserId, test.Organisation.Id);
        var second = await service.BuildAsync(test.UserId, test.Organisation.Id);

        Assert.Equal(1, first.OpenSupplierCredits);
        Assert.Equal(0, second.OpenSupplierCredits);
        var credit = await test.Db.SupplierCreditNotes.SingleAsync();
        Assert.Null(credit.SupplierBillId);
        Assert.Equal(test.Supplier.Id, credit.SupplierId);
        Assert.Equal(42.85m, credit.Total);
        Assert.Equal(first.OpeningJournalId, credit.PostedJournalId);
        Assert.Equal(1, await test.Db.PostedJournals.CountAsync(x => x.Reference.StartsWith("XERO-OPEN-")));
        var record = await test.Db.XeroMigrationRecords.SingleAsync(x => x.ExternalId == "open-credit");
        Assert.Equal(XeroMigrationRecordStatus.Imported, record.Status);
        Assert.Equal(credit.Id, record.TargetEntityId);
    }

    private static XeroMigration ApprovedMigration(AccountingTestDatabase test, DateOnly cutoverDate) => new()
    {
        OrganisationId = test.Organisation.Id, SourceOrganisationName = "Xero Test Limited",
        CutoverDate = cutoverDate, XeroEntryStopped = true, TrialBalanceReconciled = true,
        ReceivablesReconciled = true, PayablesReconciled = true, BankReconciled = true,
        FixedAssetsReconciled = true, GstReconciled = true, ManagementApproved = true,
        CreatedByUserId = test.UserId, UpdatedByUserId = test.UserId
    };

    private static async Task AddRequiredExportsAsync(
        AccountingTestDatabase test, XeroMigration migration, DatabaseImmutableDocumentStore store, string trialBalance)
    {
        foreach (var category in XeroMigrationService.RequiredCategories)
        {
            var content = category == "Trial balance"
                ? Encoding.UTF8.GetBytes(trialBalance)
                : Encoding.UTF8.GetBytes("retained source export\r\n");
            var retained = store.Stage(test.Organisation.Id, test.UserId, content);
            test.Db.XeroMigrationFiles.Add(new XeroMigrationFile
            {
                OrganisationId = test.Organisation.Id, XeroMigrationId = migration.Id, Category = category,
                FileName = category == "Trial balance" ? "trial-balance.csv" : category.Replace(' ', '-') + ".csv",
                ContentType = "text/csv", Size = content.Length, Sha256 = retained.Sha256,
                ImmutableDocumentObjectId = retained.Id, UploadedByUserId = test.UserId
            });
        }
        await test.Db.SaveChangesAsync();
    }
}
