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
}
