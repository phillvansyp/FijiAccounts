using System.Text;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
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

    [Fact]
    public async Task XeroAttachmentsAreLinkedToImportedOpenInvoicesAndBillsOnce()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.CountryCode = "NZ";
        test.Organisation.BaseCurrency = "NZD";
        var migration = ApprovedMigration(test, new DateOnly(2026, 10, 18));
        test.Db.XeroMigrations.Add(migration);
        var store = new DatabaseImmutableDocumentStore(test.Db);
        await AddRequiredExportsAsync(test, migration, store,
            "Account Code,Account,Debit,Credit\r\n1000,Bank,100.00,\r\n3000,Owner's Equity,,100.00\r\n");

        var invoiceId = Guid.NewGuid();
        var billId = Guid.NewGuid();
        await AddInvoiceSnapshotAsync(test, migration, store, "sales-invoices", invoiceId,
            "ACCREC", "INV-ATT-1", "customer", "Test Customer", "4000", 115m);
        await AddInvoiceSnapshotAsync(test, migration, store, "bills", billId,
            "ACCPAY", "BILL-ATT-1", "supplier", "Test Supplier", "6000", 57.5m);
        AddMapping(test, migration, XeroMigrationMappingKind.Contact, "customer", test.Customer.Id,
            nameof(BusinessParty), test.Customer.Name);
        AddMapping(test, migration, XeroMigrationMappingKind.Contact, "supplier", test.Supplier.Id,
            nameof(BusinessParty), test.Supplier.Name);
        AddMapping(test, migration, XeroMigrationMappingKind.Account, "4000", test.Account("4000").Id,
            nameof(LedgerAccount), "4000");
        AddMapping(test, migration, XeroMigrationMappingKind.Account, "6000", test.Account("6000").Id,
            nameof(LedgerAccount), "6000");

        var salesFile = Encoding.UTF8.GetBytes("customer invoice evidence");
        var billFile = Encoding.UTF8.GetBytes("supplier receipt evidence");
        var package = BuildAttachmentPackage(
            (invoiceId, Guid.NewGuid(), "customer-invoice.pdf", "application/pdf", salesFile),
            (billId, Guid.NewGuid(), "supplier-receipt.jpg", "image/jpeg", billFile));
        var retained = store.Stage(test.Organisation.Id, test.UserId, package);
        test.Db.XeroMigrationFiles.Add(new XeroMigrationFile
        {
            OrganisationId = test.Organisation.Id,
            XeroMigrationId = migration.Id,
            Category = "Document attachments",
            FileName = "xero-document-attachments-001.zip",
            ContentType = "application/zip",
            Size = package.Length,
            Sha256 = retained.Sha256,
            ImmutableDocumentObjectId = retained.Id,
            UploadedByUserId = test.UserId
        });
        await test.Db.SaveChangesAsync();

        var preparation = new XeroImportPreparationService(test.Db, test.Access, store);
        var service = new XeroCutoverImportService(test.Db, test.Access, store, test.Posting, preparation);
        await service.BuildAsync(test.UserId, test.Organisation.Id);
        await service.BuildAsync(test.UserId, test.Organisation.Id);

        var salesAttachment = await test.Db.SalesInvoiceAttachments.SingleAsync();
        var billAttachment = await test.Db.SupplierBillAttachments.SingleAsync();
        Assert.Equal("customer-invoice.pdf", salesAttachment.FileName);
        Assert.Equal(salesFile, salesAttachment.Content);
        Assert.Equal("supplier-receipt.jpg", billAttachment.FileName);
        Assert.Equal(billFile, billAttachment.Content);
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

    private static async Task AddInvoiceSnapshotAsync(
        AccountingTestDatabase test, XeroMigration migration, DatabaseImmutableDocumentStore store,
        string dataset, Guid sourceId, string type, string number, string contactId,
        string contactName, string accountCode, decimal total)
    {
        var root = dataset == "bills" ? "bills" : "sales-invoices";
        var json = $$"""
            {"dataset":"{{root}}","pages":[{"Invoices":[{
              "InvoiceID":"{{sourceId:D}}","InvoiceNumber":"{{number}}","Type":"{{type}}",
              "Date":"2026-10-17","DueDate":"2026-10-31","Status":"AUTHORISED",
              "CurrencyCode":"NZD","CurrencyRate":1,"SubTotal":{{total / 1.15m}},
              "TotalTax":{{total - total / 1.15m}},"Total":{{total}},"AmountDue":{{total}},
              "Contact":{"ContactID":"{{contactId}}","Name":"{{contactName}}"},
              "LineItems":[{"Description":"Imported line","Quantity":1,"UnitAmount":{{total / 1.15m}},
                "LineAmount":{{total / 1.15m}},"TaxAmount":{{total - total / 1.15m}},
                "AccountCode":"{{accountCode}}","TaxType":"OUTPUT2"}]
            }]}]}
            """;
        var bytes = Encoding.UTF8.GetBytes(json);
        var retained = store.Stage(test.Organisation.Id, test.UserId, bytes);
        var snapshot = new XeroApiSnapshot
        {
            OrganisationId = test.Organisation.Id, XeroMigrationId = migration.Id,
            DatasetKey = dataset, DisplayName = dataset, Endpoint = "Invoices",
            RecordCount = 1, PageCount = 1, Size = bytes.Length, Sha256 = retained.Sha256,
            ImmutableDocumentObjectId = retained.Id, RetrievedByUserId = test.UserId
        };
        test.Db.XeroApiSnapshots.Add(snapshot);
        test.Db.XeroMigrationRecords.Add(new XeroMigrationRecord
        {
            OrganisationId = test.Organisation.Id, XeroMigrationId = migration.Id,
            XeroApiSnapshotId = snapshot.Id, DatasetKey = dataset, ExternalId = sourceId.ToString("D"),
            DisplayName = number, SourceHash = new string('b', 64),
            Status = XeroMigrationRecordStatus.Ready, PreparedByUserId = test.UserId
        });
    }

    private static void AddMapping(
        AccountingTestDatabase test, XeroMigration migration, XeroMigrationMappingKind kind,
        string sourceKey, Guid targetId, string targetType, string targetName) =>
        test.Db.XeroMigrationMappings.Add(new XeroMigrationMapping
        {
            OrganisationId = test.Organisation.Id, XeroMigrationId = migration.Id,
            Kind = kind, SourceKey = sourceKey, SourceName = sourceKey,
            TargetEntityType = targetType, TargetEntityId = targetId,
            TargetDisplayName = targetName, Status = XeroMigrationMappingStatus.Confirmed,
            UpdatedByUserId = test.UserId
        });

    private static byte[] BuildAttachmentPackage(
        params (Guid RecordId, Guid AttachmentId, string FileName, string MimeType, byte[] Content)[] files)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            var manifest = files.Select(x => new
            {
                RecordType = "Invoice", x.RecordId, x.AttachmentId, x.FileName, x.MimeType,
                Size = x.Content.LongLength,
                Sha256 = Convert.ToHexString(SHA256.HashData(x.Content))
            }).ToArray();
            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var stream = manifestEntry.Open())
                stream.Write(JsonSerializer.SerializeToUtf8Bytes(manifest));
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(
                    $"records/Invoice/{file.RecordId:D}/{file.AttachmentId:D}-{file.FileName}");
                using var stream = entry.Open();
                stream.Write(file.Content);
            }
        }
        return output.ToArray();
    }
}
