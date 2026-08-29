using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class BankStatementImportAccountingTests
{
    [Fact]
    public async Task ImportAsync_ValidLines_PersistsExactValuesAndBatchMetadata()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank = test.Account("1000");

        var service =
            new BankStatementImportService(
                test.Db,
                test.Access);

        var lines =
            new[]
            {
                new StatementPreviewLine(
                    new DateOnly(2026, 8, 10),
                    "Customer payment",
                    "DEP-001",
                    125.50m),

                new StatementPreviewLine(
                    new DateOnly(2026, 8, 11),
                    "Office supplies",
                    "PAY-001",
                    -42.75m)
            };

        var result =
            await service.ImportAsync(
                test.UserId,
                test.Organisation.Id,
                bank.Id,
                lines,
                "Test");

        Assert.Equal(2, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.NotEqual(Guid.Empty, result.BatchId);

        var imported =
            await test.Db.BankStatementLines
                .AsNoTracking()
                .Where(x =>
                    x.OrganisationId == test.Organisation.Id &&
                    x.ImportBatchId == result.BatchId)
                .OrderBy(x => x.TransactionDate)
                .ToListAsync();

        Assert.Equal(2, imported.Count);

        Assert.Equal(
            new DateOnly(2026, 8, 10),
            imported[0].TransactionDate);

        Assert.Equal(
            "Customer payment",
            imported[0].Description);

        Assert.Equal(
            "DEP-001",
            imported[0].Reference);

        Assert.Equal(
            125.50m,
            imported[0].Amount);

        Assert.Equal(
            bank.Id,
            imported[0].BankAccountId);

        Assert.Equal(
            "Test",
            imported[0].Source);

        Assert.Equal(
            result.BatchId,
            imported[0].ImportBatchId);

        Assert.False(
            string.IsNullOrWhiteSpace(
                imported[0].SourceHash));

        Assert.Equal(
            new DateOnly(2026, 8, 11),
            imported[1].TransactionDate);

        Assert.Equal(
            "Office supplies",
            imported[1].Description);

        Assert.Equal(
            "PAY-001",
            imported[1].Reference);

        Assert.Equal(
            -42.75m,
            imported[1].Amount);
    }

    [Fact]
    public async Task ImportAsync_RepeatedTransaction_IsSkipped()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank = test.Account("1000");

        var service =
            new BankStatementImportService(
                test.Db,
                test.Access);

        var lines =
            new[]
            {
                new StatementPreviewLine(
                    new DateOnly(2026, 8, 12),
                    "Duplicate transaction",
                    "DUP-001",
                    -25m)
            };

        var first =
            await service.ImportAsync(
                test.UserId,
                test.Organisation.Id,
                bank.Id,
                lines,
                "Test");

        var second =
            await service.ImportAsync(
                test.UserId,
                test.Organisation.Id,
                bank.Id,
                lines,
                "Test");

        Assert.Equal(1, first.Imported);
        Assert.Equal(0, first.Skipped);

        Assert.Equal(0, second.Imported);
        Assert.Equal(1, second.Skipped);

        Assert.Equal(
            1,
            await test.Db.BankStatementLines
                .AsNoTracking()
                .CountAsync(x =>
                    x.OrganisationId ==
                        test.Organisation.Id &&
                    x.BankAccountId == bank.Id &&
                    x.Reference == "DUP-001"));
    }

    [Fact]
    public async Task ImportAsync_MixedDuplicateAndNewLines_ReportsCorrectCounts()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank = test.Account("1000");

        var service =
            new BankStatementImportService(
                test.Db,
                test.Access);

        var existing =
            new StatementPreviewLine(
                new DateOnly(2026, 8, 13),
                "Existing transaction",
                "MIX-001",
                50m);

        await service.ImportAsync(
            test.UserId,
            test.Organisation.Id,
            bank.Id,
            [existing],
            "Test");

        var result =
            await service.ImportAsync(
                test.UserId,
                test.Organisation.Id,
                bank.Id,
                [
                    existing,
                    new StatementPreviewLine(
                        new DateOnly(2026, 8, 14),
                        "New transaction",
                        "MIX-002",
                        -15m)
                ],
                "Test");

        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.Skipped);

        Assert.Equal(
            2,
            await test.Db.BankStatementLines
                .AsNoTracking()
                .CountAsync(x =>
                    x.OrganisationId ==
                        test.Organisation.Id &&
                    x.BankAccountId == bank.Id &&
                    (x.Reference == "MIX-001" ||
                     x.Reference == "MIX-002")));
    }

    [Fact]
    public async Task ImportAsync_SameTransactionDifferentBankAccounts_IsAllowed()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var firstBank = test.Account("1000");

        var secondBank =
    await test.BankAccounts.CreateAsync(
        test.UserId,
        new CreateBankAccountRequest(
            OrganisationId: test.Organisation.Id,
            Code: "1001",
            Name: "Second Test Bank",
            AccountNumber: null,
            OpeningBalance: 0m,
            OpeningBalanceDate: new DateOnly(2026, 8, 20)));

        var service =
            new BankStatementImportService(
                test.Db,
                test.Access);

        var lines =
            new[]
            {
                new StatementPreviewLine(
                    new DateOnly(2026, 8, 15),
                    "Shared transaction",
                    "BANK-SCOPE-001",
                    75m)
            };

        var first =
            await service.ImportAsync(
                test.UserId,
                test.Organisation.Id,
                firstBank.Id,
                lines,
                "Test");

        var second =
            await service.ImportAsync(
                test.UserId,
                test.Organisation.Id,
                secondBank.Id,
                lines,
                "Test");

        Assert.Equal(1, first.Imported);
        Assert.Equal(1, second.Imported);

        Assert.Equal(
            2,
            await test.Db.BankStatementLines
                .AsNoTracking()
                .CountAsync(x =>
                    x.OrganisationId ==
                        test.Organisation.Id &&
                    x.Reference == "BANK-SCOPE-001"));
    }

    [Fact]
    public async Task ImportAsync_NonBankAccount_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var nonBankAccount =
            test.Account("4100");

        var service =
            new BankStatementImportService(
                test.Db,
                test.Access);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.ImportAsync(
                        test.UserId,
                        test.Organisation.Id,
                        nonBankAccount.Id,
                        [
                            new StatementPreviewLine(
                                new DateOnly(2026, 8, 16),
                                "Invalid target",
                                "NONBANK-001",
                                10m)
                        ],
                        "Test"));

        Assert.Contains(
            "active bank account",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.False(
            await test.Db.BankStatementLines
                .AsNoTracking()
                .AnyAsync(x =>
                    x.Reference == "NONBANK-001"));
    }

    [Fact]
    public async Task ImportAsync_InactiveBankAccount_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank = test.Account("1000");

        bank.IsActive = false;

        await test.Db.SaveChangesAsync();

        var service =
            new BankStatementImportService(
                test.Db,
                test.Access);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.ImportAsync(
                    test.UserId,
                    test.Organisation.Id,
                    bank.Id,
                    [
                        new StatementPreviewLine(
                            new DateOnly(2026, 8, 17),
                            "Inactive bank",
                            "INACTIVE-001",
                            10m)
                    ],
                    "Test"));

        Assert.False(
            await test.Db.BankStatementLines
                .AsNoTracking()
                .AnyAsync(x =>
                    x.Reference == "INACTIVE-001"));
    }

    [Fact]
    public async Task ImportAsync_Success_CreatesSingleImportAudit()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank = test.Account("1000");

        var service =
            new BankStatementImportService(
                test.Db,
                test.Access);

        var result =
            await service.ImportAsync(
                test.UserId,
                test.Organisation.Id,
                bank.Id,
                [
                    new StatementPreviewLine(
                        new DateOnly(2026, 8, 18),
                        "Audited import",
                        "AUDIT-IMPORT-001",
                        90m)
                ],
                "CSV");

        var audits =
            await test.Db.AuditEvents
                .AsNoTracking()
                .Where(x =>
                    x.OrganisationId ==
                        test.Organisation.Id &&
                    x.EventType ==
                        "BankStatementImported" &&
                    x.EntityType ==
                        nameof(BankStatementLine) &&
                    x.EntityId ==
                        result.BatchId.ToString())
                .ToListAsync();

        Assert.Single(audits);

        Assert.Contains(
            "\"imported\":1",
            audits[0].JsonData,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "\"skipped\":0",
            audits[0].JsonData,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportAsync_AllDuplicates_DoesNotCreateSecondAudit()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank = test.Account("1000");

        var service =
            new BankStatementImportService(
                test.Db,
                test.Access);

        var lines =
            new[]
            {
                new StatementPreviewLine(
                    new DateOnly(2026, 8, 19),
                    "Audit duplicate",
                    "AUDIT-DUP-001",
                    -30m)
            };

        await service.ImportAsync(
            test.UserId,
            test.Organisation.Id,
            bank.Id,
            lines,
            "CSV");

        var before =
            await test.Db.AuditEvents
                .CountAsync(x =>
                    x.OrganisationId ==
                        test.Organisation.Id &&
                    x.EventType ==
                        "BankStatementImported");

        var second =
            await service.ImportAsync(
                test.UserId,
                test.Organisation.Id,
                bank.Id,
                lines,
                "CSV");

        var after =
            await test.Db.AuditEvents
                .CountAsync(x =>
                    x.OrganisationId ==
                        test.Organisation.Id &&
                    x.EventType ==
                        "BankStatementImported");

        Assert.Equal(0, second.Imported);
        Assert.Equal(1, second.Skipped);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task ImportAsync_IdenticalTransactionsInOneStatement_AreRetainedAndReimportDeduplicates()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var bank = test.Account("1000");
        var service = new BankStatementImportService(test.Db, test.Access);
        var duplicate = new StatementPreviewLine(
            new DateOnly(2026, 1, 20),
            "Repeated card purchase",
            null,
            -12.50m);
        StatementPreviewLine[] lines = [duplicate, duplicate];

        var first = await service.ImportAsync(
            test.UserId,
            test.Organisation.Id,
            bank.Id,
            lines,
            "PDF");
        var second = await service.ImportAsync(
            test.UserId,
            test.Organisation.Id,
            bank.Id,
            lines,
            "PDF");

        Assert.Equal(2, first.Imported);
        Assert.Equal(0, first.Skipped);
        Assert.Equal(0, second.Imported);
        Assert.Equal(2, second.Skipped);
        var stored = await test.Db.BankStatementLines
            .AsNoTracking()
            .Where(x => x.ImportBatchId == first.BatchId)
            .ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.Equal(2, stored.Select(x => x.SourceHash).Distinct().Count());
    }

    [Fact]
    public async Task GetImportBatchesAsync_GroupsImportedLinesWithDeletionStatus()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var bank = test.Account("1000");
        var service = new BankStatementImportService(test.Db, test.Access);

        var result = await service.ImportAsync(
            test.UserId,
            test.Organisation.Id,
            bank.Id,
            [
                new StatementPreviewLine(new DateOnly(2026, 6, 1), "Opening item", "JUN-1", 100m),
                new StatementPreviewLine(new DateOnly(2026, 6, 30), "Closing item", "JUN-2", -35m)
            ],
            "PDF");

        var batches = await service.GetImportBatchesAsync(
            test.UserId,
            test.Organisation.Id);

        var batch = Assert.Single(batches);
        Assert.Equal(result.BatchId, batch.BatchId);
        Assert.Equal(bank.Id, batch.BankAccountId);
        Assert.Equal(bank.Name, batch.BankAccountName);
        Assert.Equal("PDF", batch.Source);
        Assert.Equal(new DateOnly(2026, 6, 1), batch.FirstDate);
        Assert.Equal(new DateOnly(2026, 6, 30), batch.LastDate);
        Assert.Equal(2, batch.LineCount);
        Assert.Equal(65m, batch.NetAmount);
        Assert.False(batch.CanDelete);
        Assert.Equal(new DateOnly(2033, 12, 31), batch.RetainUntil);
        Assert.Null(batch.DocumentId);
        Assert.Null(batch.DocumentFileName);
    }

    [Fact]
    public async Task ImportAsync_WithStatementDocument_StoresAndScopesOriginalFile()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var bank = test.Account("1000");
        var service = new BankStatementImportService(test.Db, test.Access);
        var content = System.Text.Encoding.ASCII.GetBytes("%PDF-test-statement");

        var imported = await service.ImportAsync(
            test.UserId,
            test.Organisation.Id,
            bank.Id,
            [new StatementPreviewLine(new DateOnly(2026, 2, 20), "Card purchase", "FEB-1", -25m)],
            "PDF",
            new BankStatementDocumentRequest(
                "February card.pdf",
                "application/pdf",
                content.LongLength,
                content));

        var batch = Assert.Single(await service.GetImportBatchesAsync(
            test.UserId,
            test.Organisation.Id));
        Assert.NotNull(batch.DocumentId);
        Assert.Equal("February card.pdf", batch.DocumentFileName);

        var document = await service.GetDocumentAsync(
            test.UserId,
            test.Organisation.Id,
            imported.BatchId);
        Assert.NotNull(document);
        Assert.Equal("application/pdf", document.ContentType);
        Assert.Equal(content, document.Content);
        Assert.Null(await service.GetDocumentAsync(
            "not-a-member",
            test.Organisation.Id,
            imported.BatchId));

        await service.RecordDocumentExportAsync(
            test.UserId,
            test.Organisation.Id,
            imported.BatchId,
            document);
        Assert.True(await test.Db.AuditEvents.AnyAsync(x =>
            x.EventType == "BankStatementDocumentExported" &&
            x.EntityId == document.Id.ToString()));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteImportBatchAsync(
                test.UserId,
                test.Organisation.Id,
                imported.BatchId));
        Assert.Contains("seven-year", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(await test.Db.BankStatementImportDocuments.AnyAsync(x =>
            x.ImportBatchId == imported.BatchId));
    }

    [Fact]
    public async Task ImportAsync_InvalidStatementDocument_IsAtomic()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var bank = test.Account("1000");
        var service = new BankStatementImportService(test.Db, test.Access);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ImportAsync(
                test.UserId,
                test.Organisation.Id,
                bank.Id,
                [new StatementPreviewLine(new DateOnly(2026, 2, 20), "Card purchase", "FEB-2", -25m)],
                "PDF",
                new BankStatementDocumentRequest(
                    "February card.pdf",
                    "application/pdf",
                    4,
                    [1, 2, 3, 4])));

        Assert.Empty(await test.Db.BankStatementLines.AsNoTracking().ToListAsync());
        Assert.Empty(await test.Db.BankStatementImportDocuments.AsNoTracking().ToListAsync());
        Assert.Empty(await test.Db.AuditEvents.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task DeleteImportBatchAsync_UnreconciledBatch_DeletesLinesAndCreatesAudit()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var bank = test.Account("1000");
        var service = new BankStatementImportService(test.Db, test.Access);
        var imported = await service.ImportAsync(
            test.UserId,
            test.Organisation.Id,
            bank.Id,
            [new StatementPreviewLine(new DateOnly(2010, 6, 12), "Wrong import", "WRONG-1", -45m)],
            "CSV");

        var result = await service.DeleteImportBatchAsync(
            test.UserId,
            test.Organisation.Id,
            imported.BatchId);

        Assert.Equal(1, result.Deleted);
        Assert.False(await test.Db.BankStatementLines.AnyAsync(x =>
            x.OrganisationId == test.Organisation.Id &&
            x.ImportBatchId == imported.BatchId));

        var audit = await test.Db.AuditEvents.AsNoTracking().SingleAsync(x =>
            x.OrganisationId == test.Organisation.Id &&
            x.EventType == "BankStatementImportDeleted" &&
            x.EntityId == imported.BatchId.ToString());
        Assert.Contains("\"LineCount\":1", audit.JsonData);
    }

    [Fact]
    public async Task DeleteImportBatchAsync_ReconciledLine_RejectsEntireDeletion()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var bank = test.Account("1000");
        var service = new BankStatementImportService(test.Db, test.Access);
        var imported = await service.ImportAsync(
            test.UserId,
            test.Organisation.Id,
            bank.Id,
            [new StatementPreviewLine(new DateOnly(2026, 6, 14), "Already used", "USED-1", 75m)],
            "CSV");

        var line = await test.Db.BankStatementLines.SingleAsync(x =>
            x.ImportBatchId == imported.BatchId);
        line.ReconciledAt = DateTimeOffset.UtcNow;
        line.ReconciledByUserId = test.UserId;
        await test.Db.SaveChangesAsync();

        var batches = await service.GetImportBatchesAsync(
            test.UserId,
            test.Organisation.Id);
        Assert.False(Assert.Single(batches).CanDelete);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteImportBatchAsync(
                test.UserId,
                test.Organisation.Id,
                imported.BatchId));

        Assert.Contains("reconciled", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(await test.Db.BankStatementLines.AnyAsync(x =>
            x.ImportBatchId == imported.BatchId));
    }

    [Fact]
    public async Task DeleteImportBatchAsync_UserWithoutPostingAccess_IsRejected()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var bank = test.Account("1000");
        var service = new BankStatementImportService(test.Db, test.Access);
        var imported = await service.ImportAsync(
            test.UserId,
            test.Organisation.Id,
            bank.Id,
            [new StatementPreviewLine(new DateOnly(2026, 6, 20), "Protected import", "SEC-1", 25m)],
            "CSV");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.DeleteImportBatchAsync(
                "not-a-member",
                test.Organisation.Id,
                imported.BatchId));

        Assert.True(await test.Db.BankStatementLines.AnyAsync(x =>
            x.ImportBatchId == imported.BatchId));
    }

    [Fact]
public async Task ReadAsync_CsvAmountColumn_ParsesTransactions()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var service =
        new BankStatementImportService(
            test.Db,
            test.Access);

    var csv =
        """
        Date,Description,Reference,Amount
        2026-08-10,Customer payment,DEP-001,125.50
        2026-08-11,Office supplies,PAY-001,-42.75
        """;

    await using var stream =
        new MemoryStream(
            System.Text.Encoding.UTF8.GetBytes(csv));

    var preview =
        await service.ReadAsync(
            stream,
            "statement.csv");

    Assert.Equal("CSV", preview.Format);
    Assert.Equal(2, preview.Lines.Count);

    Assert.Equal(
        new DateOnly(2026, 8, 10),
        preview.Lines[0].Date);

    Assert.Equal(
        "Customer payment",
        preview.Lines[0].Description);

    Assert.Equal(
        "DEP-001",
        preview.Lines[0].Reference);

    Assert.Equal(
        125.50m,
        preview.Lines[0].Amount);

    Assert.Equal(
        -42.75m,
        preview.Lines[1].Amount);
}

[Fact]
public async Task ReadAsync_CsvDebitAndCreditColumns_NormalizesDirection()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var service =
        new BankStatementImportService(
            test.Db,
            test.Access);

    var csv =
        """
        Date,Description,Debit,Credit
        20/08/2026,Purchase,25.00,
        21/08/2026,Deposit,,100.00
        """;

    await using var stream =
        new MemoryStream(
            System.Text.Encoding.UTF8.GetBytes(csv));

    var preview =
        await service.ReadAsync(
            stream,
            "statement.csv");

    Assert.Equal(2, preview.Lines.Count);

    Assert.Equal(
        -25m,
        preview.Lines[0].Amount);

    Assert.Equal(
        100m,
        preview.Lines[1].Amount);
}

[Fact]
public async Task ReadAsync_CsvQuotedDescription_PreservesComma()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var service =
        new BankStatementImportService(
            test.Db,
            test.Access);

    var csv =
        """
        Date,Description,Amount
        2026-08-20,"Supermarket, Suva",-50.00
        """;

    await using var stream =
        new MemoryStream(
            System.Text.Encoding.UTF8.GetBytes(csv));

    var preview =
        await service.ReadAsync(
            stream,
            "statement.csv");

    var line =
        Assert.Single(preview.Lines);

    Assert.Equal(
        "Supermarket, Suva",
        line.Description);

    Assert.Equal(
        -50m,
        line.Amount);
}

[Fact]
public async Task ReadAsync_CsvZeroAmountRow_IsIgnored()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var service =
        new BankStatementImportService(
            test.Db,
            test.Access);

    var csv =
        """
        Date,Description,Amount
        2026-08-20,Zero transaction,0.00
        2026-08-21,Real transaction,25.00
        """;

    await using var stream =
        new MemoryStream(
            System.Text.Encoding.UTF8.GetBytes(csv));

    var preview =
        await service.ReadAsync(
            stream,
            "statement.csv");

    var line =
        Assert.Single(preview.Lines);

    Assert.Equal(
        "Real transaction",
        line.Description);

    Assert.Equal(
        25m,
        line.Amount);
}

[Fact]
public async Task ReadAsync_CsvInvalidDate_IsRejected()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var service =
        new BankStatementImportService(
            test.Db,
            test.Access);

    var csv =
        """
        Date,Description,Amount
        not-a-date,Invalid transaction,10.00
        """;

    await using var stream =
        new MemoryStream(
            System.Text.Encoding.UTF8.GetBytes(csv));

    var ex =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.ReadAsync(
                    stream,
                    "statement.csv"));

    Assert.Contains(
        "invalid date",
        ex.Message,
        StringComparison.OrdinalIgnoreCase);
}

[Fact]
public async Task ReadAsync_CsvWithoutAmountColumns_IsRejected()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var service =
        new BankStatementImportService(
            test.Db,
            test.Access);

    var csv =
        """
        Date,Description,Reference
        2026-08-20,Missing amount,TEST-001
        """;

    await using var stream =
        new MemoryStream(
            System.Text.Encoding.UTF8.GetBytes(csv));

    var ex =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.ReadAsync(
                    stream,
                    "statement.csv"));

    Assert.Contains(
        "Amount or Debit and Credit",
        ex.Message,
        StringComparison.OrdinalIgnoreCase);
}
}
