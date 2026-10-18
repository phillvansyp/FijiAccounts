using System.Text;
using System.Text.Json;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class XeroMigrationAssistantServiceTests
{
    [Fact]
    public async Task CopiedSourceRecommendsGuidedPreparationBeforeEvidenceAndApproval()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var store = new DatabaseImmutableDocumentStore(test.Db);
        var migration = Migration(test, XeroMigrationStatus.Preparing);
        test.Db.XeroMigrations.Add(migration);
        await AddSnapshotAsync(test, store, migration, "accounts", "{\"Accounts\":[]}");
        await test.Db.SaveChangesAsync();

        var preparation = new XeroImportPreparationService(test.Db, test.Access, store);
        var service = new XeroMigrationAssistantService(test.Db, test.Access, preparation);

        var result = await service.GetAsync(test.UserId, test.Organisation.Id);

        Assert.Equal(XeroMigrationAssistantAction.RunPreparation, result.NextAction);
        Assert.Contains(result.Checks, x => x.Key == "source" && x.Passed);
        Assert.Contains(result.Checks, x => x.Key == "preparation" && !x.Passed);
        Assert.False(result.PostCutoverVerified);
    }

    [Fact]
    public async Task CompletedBalancedCutoverPassesAutomaticFinalVerification()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var store = new DatabaseImmutableDocumentStore(test.Db);
        var migration = Migration(test, XeroMigrationStatus.Complete);
        migration.XeroEntryStopped = migration.TrialBalanceReconciled = migration.ReceivablesReconciled = true;
        migration.PayablesReconciled = migration.BankReconciled = migration.FixedAssetsReconciled = true;
        migration.GstReconciled = migration.ManagementApproved = true;
        test.Db.XeroMigrations.Add(migration);

        foreach (var category in XeroMigrationService.RequiredCategories)
            await AddEvidenceAsync(test, store, migration, category);
        var snapshot = await AddSnapshotAsync(test, store, migration, "accounts", "{\"Accounts\":[]}");
        test.Db.XeroMigrationRecords.AddRange(
            new XeroMigrationRecord
            {
                OrganisationId = test.Organisation.Id, XeroMigrationId = migration.Id,
                XeroApiSnapshotId = snapshot.Id, DatasetKey = "accounts", ExternalId = "bank",
                DisplayName = "Bank", SourceHash = new string('a', 64),
                Status = XeroMigrationRecordStatus.Imported, TargetEntityType = nameof(LedgerAccount),
                TargetEntityId = await test.Db.LedgerAccounts.Select(x => x.Id).FirstAsync(),
                PreparedByUserId = test.UserId, ImportedByUserId = test.UserId, ImportedAt = DateTimeOffset.UtcNow
            },
            new XeroMigrationRecord
            {
                OrganisationId = test.Organisation.Id, XeroMigrationId = migration.Id,
                XeroApiSnapshotId = snapshot.Id, DatasetKey = "journals", ExternalId = "history",
                DisplayName = "Historical journal", SourceHash = new string('b', 64),
                Status = XeroMigrationRecordStatus.Archived, PreparedByUserId = test.UserId
            });
        test.Db.XeroMigrationImportRuns.Add(new XeroMigrationImportRun
        {
            OrganisationId = test.Organisation.Id, XeroMigrationId = migration.Id,
            Status = XeroMigrationImportRunStatus.Completed, SourceRecordCount = 2, ImportedCount = 1,
            StartedByUserId = test.UserId, CompletedAt = DateTimeOffset.UtcNow
        });

        var accounts = await test.Db.LedgerAccounts.Take(2).ToListAsync();
        var journal = new PostedJournal
        {
            OrganisationId = test.Organisation.Id, SequenceNumber = 1,
            EntryDate = migration.CutoverDate!.Value.AddDays(-1), Reference = "XERO-OPEN-20261018",
            Currency = "NZD", PostedAt = DateTimeOffset.UtcNow, PostedByUserId = test.UserId,
            Lines =
            [
                new PostedJournalLine { LedgerAccountId = accounts[0].Id, Description = "Opening", Debit = 100m },
                new PostedJournalLine { LedgerAccountId = accounts[1].Id, Description = "Opening", Credit = 100m }
            ]
        };
        test.Db.PostedJournals.Add(journal);
        test.Db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = test.Organisation.Id, EventType = "XeroOpeningBalancePosted",
            EntityType = nameof(XeroMigration), EntityId = migration.Id.ToString(), UserId = test.UserId,
            JsonData = JsonSerializer.Serialize(new { OpeningJournalId = journal.Id.ToString() })
        });
        await test.Db.SaveChangesAsync();

        var preparation = new XeroImportPreparationService(test.Db, test.Access, store);
        var service = new XeroMigrationAssistantService(test.Db, test.Access, preparation);

        var result = await service.GetAsync(test.UserId, test.Organisation.Id);

        Assert.Equal(XeroMigrationAssistantAction.Finished, result.NextAction);
        Assert.True(result.CutoverExists);
        Assert.True(result.PostCutoverVerified);
        Assert.Equal(result.TotalCount, result.PassedCount);
        Assert.Contains(result.Checks, x => x.Key == "opening" && x.Passed);
        Assert.Contains(result.Checks, x => x.Key == "archive" && x.Passed);

        await service.VerifyAndRecordAsync(test.UserId, test.Organisation.Id);
        Assert.Single(await test.Db.AuditEvents.Where(x =>
            x.EventType == "XeroMigrationPostCutoverVerified").ToListAsync());
    }

    private static XeroMigration Migration(AccountingTestDatabase test, XeroMigrationStatus status) => new()
    {
        OrganisationId = test.Organisation.Id,
        SourceOrganisationName = "Xero Test Limited",
        CutoverDate = new DateOnly(2026, 10, 18),
        Status = status,
        CreatedByUserId = test.UserId,
        UpdatedByUserId = test.UserId
    };

    private static async Task<XeroApiSnapshot> AddSnapshotAsync(
        AccountingTestDatabase test, DatabaseImmutableDocumentStore store, XeroMigration migration,
        string dataset, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var retained = store.Stage(test.Organisation.Id, test.UserId, bytes);
        var snapshot = new XeroApiSnapshot
        {
            OrganisationId = test.Organisation.Id, XeroMigrationId = migration.Id,
            DatasetKey = dataset, DisplayName = dataset, Endpoint = dataset,
            RecordCount = 0, PageCount = 1, Size = bytes.Length, Sha256 = retained.Sha256,
            ImmutableDocumentObjectId = retained.Id, RetrievedByUserId = test.UserId
        };
        test.Db.XeroApiSnapshots.Add(snapshot);
        return snapshot;
    }

    private static Task AddEvidenceAsync(
        AccountingTestDatabase test, DatabaseImmutableDocumentStore store, XeroMigration migration, string category)
    {
        var bytes = Encoding.UTF8.GetBytes(category);
        var retained = store.Stage(test.Organisation.Id, test.UserId, bytes);
        test.Db.XeroMigrationFiles.Add(new XeroMigrationFile
        {
            OrganisationId = test.Organisation.Id, XeroMigrationId = migration.Id,
            Category = category, FileName = category + ".csv", ContentType = "text/csv",
            Size = bytes.Length, Sha256 = retained.Sha256, ImmutableDocumentObjectId = retained.Id,
            UploadedByUserId = test.UserId
        });
        return Task.CompletedTask;
    }
}
