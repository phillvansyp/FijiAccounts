using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class XeroMigrationServiceTests
{
    [Fact]
    public async Task SetupUsesCutoverAsOrganisationConversionDateAndAuditsChange()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.CountryCode = "NZ";
        await test.Db.SaveChangesAsync();
        var service = Service(test);
        var cutover = new DateOnly(2026, 11, 1);

        var migration = await service.SaveAsync(test.UserId, test.Organisation.Id, Request(cutover));

        Assert.Equal(cutover, migration.CutoverDate);
        Assert.True(migration.IncludeFullHistory);
        Assert.Equal(cutover, test.Organisation.ConversionDate);
        Assert.True(await test.Db.AuditEvents.AnyAsync(x =>
            x.OrganisationId == test.Organisation.Id && x.EventType == "XeroMigrationCreated"));
    }

    [Fact]
    public async Task UploadRetainsExactFileAndItsFingerprint()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.CountryCode = "NZ";
        await test.Db.SaveChangesAsync();
        var service = Service(test);
        await service.SaveAsync(test.UserId, test.Organisation.Id, Request(new DateOnly(2026, 11, 1)));
        var content = "Account Code,Account Name\n1000,Bank"u8.ToArray();

        var file = await service.AddFileAsync(
            test.UserId, test.Organisation.Id, "Chart of accounts", "ChartOfAccounts.csv", "text/csv", content);
        var download = await service.ReadFileAsync(test.UserId, test.Organisation.Id, file.Id);

        Assert.NotNull(download);
        Assert.Equal(content, download.Content);
        Assert.Equal(64, file.Sha256.Length);
        Assert.True(await test.Db.AuditEvents.AnyAsync(x => x.EntityId == file.Id.ToString() && x.EventType == "XeroMigrationFileAdded"));
    }

    [Fact]
    public async Task ReadinessRequiresEveryRequiredSourceSet()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.CountryCode = "NZ";
        await test.Db.SaveChangesAsync();
        var service = Service(test);
        await service.SaveAsync(test.UserId, test.Organisation.Id, Request(new DateOnly(2026, 11, 1)));

        foreach (var category in XeroMigrationService.RequiredCategories)
        {
            await service.AddFileAsync(
                test.UserId, test.Organisation.Id, category, $"{Guid.NewGuid():N}.csv", "text/csv", "A,B\n1,2"u8.ToArray());
        }

        var migration = await service.GetAsync(test.UserId, test.Organisation.Id);
        Assert.Equal(XeroMigrationStatus.ReadyForImport, migration!.Status);
    }

    [Fact]
    public async Task ApprovalRequiresEveryReconciliationControl()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.CountryCode = "NZ";
        await test.Db.SaveChangesAsync();
        var service = Service(test);
        var cutover = new DateOnly(2026, 11, 1);
        await service.SaveAsync(test.UserId, test.Organisation.Id, Request(cutover));
        await AddRequiredExportsAsync(test, service);

        var approved = await service.SaveAsync(test.UserId, test.Organisation.Id, Request(cutover) with
        {
            XeroEntryStopped = true,
            TrialBalanceReconciled = true,
            ReceivablesReconciled = true,
            PayablesReconciled = true,
            BankReconciled = true,
            FixedAssetsReconciled = true,
            GstReconciled = true,
            ManagementApproved = true
        });

        Assert.Equal(XeroMigrationStatus.ApprovedForCutover, approved.Status);
    }

    [Fact]
    public async Task IncompleteControlsCannotBeManagementApproved()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.CountryCode = "NZ";
        await test.Db.SaveChangesAsync();
        var service = Service(test);

        var migration = await service.SaveAsync(test.UserId, test.Organisation.Id,
            Request(new DateOnly(2026, 11, 1)) with { ManagementApproved = true });

        Assert.False(migration.ManagementApproved);
        Assert.NotEqual(XeroMigrationStatus.ApprovedForCutover, migration.Status);
    }

    [Fact]
    public async Task RestartClearsUnpostedProgressAndSelectedTenantButRetainsProtectedDocuments()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.CountryCode = "NZ";
        await test.Db.SaveChangesAsync();
        var service = Service(test);
        var cutover = new DateOnly(2026, 11, 1);
        var migration = await service.SaveAsync(test.UserId, test.Organisation.Id, Request(cutover));
        var file = await service.AddFileAsync(test.UserId, test.Organisation.Id,
            "Chart of accounts", "accounts.csv", "text/csv", "Code,Name\n100,Bank"u8.ToArray());
        var retained = new DatabaseImmutableDocumentStore(test.Db).Stage(
            test.Organisation.Id, test.UserId, "{}"u8.ToArray());
        var snapshot = new XeroApiSnapshot
        {
            OrganisationId = test.Organisation.Id,
            XeroMigrationId = migration.Id,
            DatasetKey = "accounts",
            DisplayName = "Chart of accounts",
            Endpoint = "Accounts",
            ImmutableDocumentObjectId = retained.Id,
            Sha256 = retained.Sha256,
            Size = 2,
            RetrievedByUserId = test.UserId
        };
        test.Db.XeroApiSnapshots.Add(snapshot);
        test.Db.XeroMigrationRecords.Add(new XeroMigrationRecord
        {
            OrganisationId = test.Organisation.Id,
            XeroMigrationId = migration.Id,
            XeroApiSnapshotId = snapshot.Id,
            DatasetKey = "accounts",
            ExternalId = "account-1",
            DisplayName = "100 · Bank",
            SourceHash = new string('a', 64),
            PreparedByUserId = test.UserId
        });
        var tenantId = Guid.NewGuid();
        test.Db.XeroConnections.Add(new XeroConnection
        {
            OrganisationId = test.Organisation.Id,
            XeroConnectionId = "connection",
            XeroTenantId = tenantId,
            XeroTenantName = "Wrong organisation",
            ProtectedAccessToken = "protected",
            ProtectedRefreshToken = "protected",
            GrantedScopes = "accounting.settings.read",
            ConnectedByUserId = test.UserId,
            UpdatedByUserId = test.UserId
        });
        await test.Db.SaveChangesAsync();
        var protectedDocumentCount = await test.Db.ImmutableDocumentObjects.CountAsync();

        var result = await service.RestartAsync(test.UserId, test.Organisation.Id);

        Assert.Equal(0, result.RetainedMasterRecords);
        var restarted = await service.GetAsync(test.UserId, test.Organisation.Id);
        Assert.NotNull(restarted);
        Assert.Empty(restarted.SourceOrganisationName);
        Assert.Null(restarted.CutoverDate);
        Assert.Equal(XeroMigrationStatus.Preparing, restarted.Status);
        Assert.Empty(restarted.Files);
        Assert.Empty(restarted.ApiSnapshots);
        Assert.Equal(1, await test.Db.XeroMigrationFiles.CountAsync());
        Assert.Equal(1, await test.Db.XeroApiSnapshots.CountAsync());
        Assert.Empty(await test.Db.XeroMigrationRecords.ToListAsync());
        var connection = await test.Db.XeroConnections.SingleAsync();
        Assert.Null(connection.XeroTenantId);
        Assert.Null(connection.XeroTenantName);
        Assert.Equal(protectedDocumentCount, await test.Db.ImmutableDocumentObjects.CountAsync());
        Assert.Null((await test.Db.Organisations.SingleAsync(x => x.Id == test.Organisation.Id)).ConversionDate);
        Assert.True(await test.Db.AuditEvents.AnyAsync(x => x.EventType == "XeroMigrationRestarted"));
        Assert.True(await test.Db.ImmutableDocumentObjects.AnyAsync(x => x.Id == file.ImmutableDocumentObjectId));

        await service.SaveAsync(test.UserId, test.Organisation.Id, Request(cutover));
        await service.AddFileAsync(test.UserId, test.Organisation.Id,
            "Trial balance", "new-attempt.csv", "text/csv", "Account,Balance\n100,1"u8.ToArray());
        var nextAttempt = await service.GetAsync(test.UserId, test.Organisation.Id);
        Assert.Single(nextAttempt!.Files);
        Assert.Equal("new-attempt.csv", nextAttempt.Files[0].FileName);
        Assert.Equal(2, await test.Db.XeroMigrationFiles.CountAsync());
    }

    [Fact]
    public async Task RestartIsBlockedAfterTheLiveOpeningJournalWasPosted()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.CountryCode = "NZ";
        await test.Db.SaveChangesAsync();
        var service = Service(test);
        var migration = await service.SaveAsync(test.UserId, test.Organisation.Id,
            Request(new DateOnly(2026, 11, 1)));
        test.Db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = test.Organisation.Id,
            EventType = "XeroOpeningBalancePosted",
            EntityType = nameof(XeroMigration),
            EntityId = migration.Id.ToString(),
            UserId = test.UserId,
            JsonData = "{}"
        });
        await test.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RestartAsync(test.UserId, test.Organisation.Id));

        Assert.Contains("posted its live cutover", error.Message);
        Assert.Equal("JCR 2006 Limited", (await service.GetAsync(test.UserId, test.Organisation.Id))!.SourceOrganisationName);
    }

    [Fact]
    public async Task LiveImportPendingDoesNotPreventApprovalNeededToRunCutover()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.CountryCode = "NZ";
        await test.Db.SaveChangesAsync();
        var service = Service(test);
        var cutover = new DateOnly(2026, 11, 1);
        var migration = await service.SaveAsync(test.UserId, test.Organisation.Id, Request(cutover));
        await AddRequiredExportsAsync(test, service);
        var retained = new DatabaseImmutableDocumentStore(test.Db).Stage(
            test.Organisation.Id, test.UserId, "{}"u8.ToArray());
        var snapshot = new XeroApiSnapshot
        {
            OrganisationId = test.Organisation.Id,
            XeroMigrationId = migration.Id,
            DatasetKey = "sales-invoices",
            DisplayName = "Sales invoices",
            Endpoint = "Invoices",
            ImmutableDocumentObjectId = retained.Id,
            Sha256 = retained.Sha256,
            Size = 2,
            RetrievedByUserId = test.UserId
        };
        test.Db.XeroApiSnapshots.Add(snapshot);
        test.Db.XeroMigrationRecords.Add(new XeroMigrationRecord
        {
            OrganisationId = test.Organisation.Id,
            XeroMigrationId = migration.Id,
            XeroApiSnapshotId = snapshot.Id,
            DatasetKey = "sales-invoices",
            ExternalId = "invoice-1",
            DisplayName = "INV-1",
            SourceHash = new string('1', 64),
            PreparedByUserId = test.UserId
        });
        test.Db.XeroMigrationIssues.Add(new XeroMigrationIssue
        {
            OrganisationId = test.Organisation.Id,
            XeroMigrationId = migration.Id,
            Code = "LIVE_IMPORT_PENDING",
            Severity = XeroMigrationIssueSeverity.Blocking,
            Message = "One invoice still needs conversion."
        });
        await test.Db.SaveChangesAsync();

        var result = await service.SaveAsync(test.UserId, test.Organisation.Id, Request(cutover) with
        {
            XeroEntryStopped = true,
            TrialBalanceReconciled = true,
            ReceivablesReconciled = true,
            PayablesReconciled = true,
            BankReconciled = true,
            FixedAssetsReconciled = true,
            GstReconciled = true,
            ManagementApproved = true
        });

        Assert.True(result.ManagementApproved);
        Assert.Equal(XeroMigrationStatus.ApprovedForCutover, result.Status);
    }

    [Fact]
    public async Task AccountantCanPrepareButCannotGiveManagementApproval()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.CountryCode = "NZ";
        var membership = await test.Db.OrganisationMemberships.SingleAsync(x =>
            x.OrganisationId == test.Organisation.Id && x.UserId == test.UserId);
        membership.Role = OrganisationRole.Accountant;
        await test.Db.SaveChangesAsync();
        var service = Service(test);
        var cutover = new DateOnly(2026, 11, 1);

        await service.SaveAsync(test.UserId, test.Organisation.Id, Request(cutover));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveAsync(
            test.UserId, test.Organisation.Id, Request(cutover) with
            {
                XeroEntryStopped = true,
                TrialBalanceReconciled = true,
                ReceivablesReconciled = true,
                PayablesReconciled = true,
                BankReconciled = true,
                FixedAssetsReconciled = true,
                GstReconciled = true,
                ManagementApproved = true
            }));
    }

    [Theory]
    [InlineData("bad.exe", "plain text")]
    [InlineData("fake.pdf", "not a pdf")]
    [InlineData("../accounts.csv", "A,B")]
    public async Task UnsafeSourceFilesAreRejected(string filename, string content)
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.CountryCode = "NZ";
        await test.Db.SaveChangesAsync();
        var service = Service(test);
        await service.SaveAsync(test.UserId, test.Organisation.Id, Request(new DateOnly(2026, 11, 1)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddFileAsync(
            test.UserId, test.Organisation.Id, "General ledger", filename, null, System.Text.Encoding.UTF8.GetBytes(content)));
    }

    [Fact]
    public void CompleteApiSnapshotsCountAsEvidenceWithoutDuplicateUploads()
    {
        var migration = new XeroMigration { CreatedByUserId = "user", UpdatedByUserId = "user" };
        foreach (var key in new[] { "trial-balance", "balance-sheet", "profit-loss", "accounts", "contacts",
                     "sales-invoices", "sales-credits", "bills", "supplier-credits", "payments", "prepayments", "overpayments" })
        {
            migration.ApiSnapshots.Add(new XeroApiSnapshot
            {
                XeroMigrationId = migration.Id,
                DatasetKey = key,
                DisplayName = key,
                Endpoint = key,
                Sha256 = new string('a', 64),
                RetrievedByUserId = "user"
            });
        }

        Assert.True(XeroMigrationService.HasRequiredEvidenceCategory(migration, "Trial balance"));
        Assert.True(XeroMigrationService.HasRequiredEvidenceCategory(migration, "Balance Sheet and Profit & Loss"));
        Assert.True(XeroMigrationService.HasRequiredEvidenceCategory(migration, "Chart of accounts"));
        Assert.True(XeroMigrationService.HasRequiredEvidenceCategory(migration, "Contacts"));
        Assert.True(XeroMigrationService.HasRequiredEvidenceCategory(migration, "Sales invoices, credits and allocations"));
        Assert.True(XeroMigrationService.HasRequiredEvidenceCategory(migration, "Bills, supplier credits and allocations"));
        Assert.False(XeroMigrationService.HasRequiredEvidenceCategory(migration, "Bank statements and reconciliation"));
    }

    private static XeroMigrationService Service(AccountingTestDatabase test) =>
        new(test.Db, test.Access, new DatabaseImmutableDocumentStore(test.Db));

    private static async Task AddRequiredExportsAsync(AccountingTestDatabase test, XeroMigrationService service)
    {
        foreach (var category in XeroMigrationService.RequiredCategories)
            await service.AddFileAsync(test.UserId, test.Organisation.Id, category,
                $"{Guid.NewGuid():N}.csv", "text/csv", "A,B\n1,2"u8.ToArray());
    }

    private static SaveXeroMigrationRequest Request(DateOnly cutoverDate) => new(
        "JCR 2006 Limited", cutoverDate, true, false, false, false, false, false, false, false, false, null);
}
