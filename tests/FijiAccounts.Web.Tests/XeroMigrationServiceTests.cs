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
