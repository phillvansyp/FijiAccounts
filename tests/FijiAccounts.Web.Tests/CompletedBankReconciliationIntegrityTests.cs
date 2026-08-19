using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class CompletedBankReconciliationIntegrityTests
{
    [Fact]
    public async Task AddStatementLine_InsideCompletedReconciliation_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank = test.Account("1000");

        var sessionService =
            new BankReconciliationSessionService(
                test.Db,
                test.Access);

        var session =
            await sessionService.CreateAsync(
                test.UserId,
                new BankReconciliationSessionRequest(
                    test.Organisation.Id,
                    bank.Id,
                    new DateOnly(2026, 8, 1),
                    new DateOnly(2026, 8, 31),
                    0m,
                    0m));

        await sessionService.CompleteAsync(
            test.UserId,
            test.Organisation.Id,
            session.Id);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    test.Reconciliation.AddStatementLineAsync(
                        test.UserId,
                        new StatementLineRequest(
                            test.Organisation.Id,
                            bank.Id,
                            new DateOnly(2026, 8, 15),
                            "Late statement item",
                            "LOCKED-STMT-001",
                            100m)));

        Assert.Contains(
            "completed reconciliation",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.False(
            await test.Db.BankStatementLines
                .AsNoTracking()
                .AnyAsync(x =>
                    x.OrganisationId == test.Organisation.Id &&
                    x.Reference == "LOCKED-STMT-001"));
    }

    [Fact]
    public async Task AddStatementLine_OutsideCompletedReconciliation_IsAllowed()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank = test.Account("1000");

        var sessionService =
            new BankReconciliationSessionService(
                test.Db,
                test.Access);

        var session =
            await sessionService.CreateAsync(
                test.UserId,
                new BankReconciliationSessionRequest(
                    test.Organisation.Id,
                    bank.Id,
                    new DateOnly(2026, 8, 1),
                    new DateOnly(2026, 8, 31),
                    0m,
                    0m));

        await sessionService.CompleteAsync(
            test.UserId,
            test.Organisation.Id,
            session.Id);

        var statement =
            await test.Reconciliation.AddStatementLineAsync(
                test.UserId,
                new StatementLineRequest(
                    test.Organisation.Id,
                    bank.Id,
                    new DateOnly(2026, 9, 1),
                    "September item",
                    "OPEN-STMT-001",
                    100m));

        Assert.Equal(
            new DateOnly(2026, 9, 1),
            statement.TransactionDate);
    }

    [Fact]
    public async Task Import_InsideCompletedReconciliation_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank = test.Account("1000");

        var sessionService =
            new BankReconciliationSessionService(
                test.Db,
                test.Access);

        var session =
            await sessionService.CreateAsync(
                test.UserId,
                new BankReconciliationSessionRequest(
                    test.Organisation.Id,
                    bank.Id,
                    new DateOnly(2026, 8, 1),
                    new DateOnly(2026, 8, 31),
                    0m,
                    0m));

        await sessionService.CompleteAsync(
            test.UserId,
            test.Organisation.Id,
            session.Id);

        var import =
            new BankStatementImportService(
                test.Db,
                test.Access);

        var lines =
            new[]
            {
                new StatementPreviewLine(
                    new DateOnly(2026, 8, 20),
                    "Late imported item",
                    "LOCKED-IMPORT-001",
                    50m)
            };

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    import.ImportAsync(
                        test.UserId,
                        test.Organisation.Id,
                        bank.Id,
                        lines,
                        "Test"));

        Assert.Contains(
            "completed reconciliation",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.False(
            await test.Db.BankStatementLines
                .AsNoTracking()
                .AnyAsync(x =>
                    x.OrganisationId == test.Organisation.Id &&
                    x.Reference == "LOCKED-IMPORT-001"));
    }

    [Fact]
    public async Task Import_OutsideCompletedReconciliation_IsAllowed()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank = test.Account("1000");

        var sessionService =
            new BankReconciliationSessionService(
                test.Db,
                test.Access);

        var session =
            await sessionService.CreateAsync(
                test.UserId,
                new BankReconciliationSessionRequest(
                    test.Organisation.Id,
                    bank.Id,
                    new DateOnly(2026, 8, 1),
                    new DateOnly(2026, 8, 31),
                    0m,
                    0m));

        await sessionService.CompleteAsync(
            test.UserId,
            test.Organisation.Id,
            session.Id);

        var import =
            new BankStatementImportService(
                test.Db,
                test.Access);

        var lines =
            new[]
            {
                new StatementPreviewLine(
                    new DateOnly(2026, 9, 1),
                    "September imported item",
                    "OPEN-IMPORT-001",
                    50m)
            };

        var result =
            await import.ImportAsync(
                test.UserId,
                test.Organisation.Id,
                bank.Id,
                lines,
                "Test");

        Assert.Equal(1, result.Imported);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
public async Task ReconcileStatementLine_InsideCompletedReconciliation_IsRejected()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var bank = test.Account("1000");
    var income = test.Account("4100");

    var journal =
        await test.Posting.PostAsync(
            test.UserId,
            new JournalPostRequest(
                test.Organisation.Id,
                new DateOnly(2026, 8, 15),
                "RECON-LOCK-001",
                "Deposit",
                [
                    new(
                        bank.Id,
                        "Deposit",
                        100m,
                        0m),
                    new(
                        income.Id,
                        "Deposit",
                        0m,
                        100m)
                ]));

    var statement =
        new BankStatementLine
        {
            OrganisationId = test.Organisation.Id,
            BankAccountId = bank.Id,
            TransactionDate = new DateOnly(2026, 8, 15),
            Description = "Historical statement item",
            Reference = "RECON-LOCK-STMT",
            Amount = 100m,
            Source = "Test"
        };

    test.Db.BankStatementLines.Add(statement);

    await test.Db.SaveChangesAsync();

    var bankLine =
        await test.Db.PostedJournalLines
            .SingleAsync(x =>
                x.PostedJournalId == journal.Id &&
                x.LedgerAccountId == bank.Id);

    var sessionService =
        new BankReconciliationSessionService(
            test.Db,
            test.Access);

    var session =
        await sessionService.CreateAsync(
            test.UserId,
            new BankReconciliationSessionRequest(
                test.Organisation.Id,
                bank.Id,
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 31),
                0m,
                100m));

    // Mark the historical line as reconciled so the session can complete.
    statement.MatchedPostedJournalLineId = bankLine.Id;
    statement.ReconciledAt = DateTimeOffset.UtcNow;
    statement.ReconciledByUserId = test.UserId;

    await test.Db.SaveChangesAsync();

    await sessionService.CompleteAsync(
        test.UserId,
        test.Organisation.Id,
        session.Id);

    // Simulate an old/incomplete line existing inside the now-completed period.
    statement.MatchedPostedJournalLineId = null;
    statement.ReconciledAt = null;
    statement.ReconciledByUserId = null;

    await test.Db.SaveChangesAsync();

    var exception =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                test.Reconciliation.ReconcileAsync(
                    test.UserId,
                    test.Organisation.Id,
                    statement.Id,
                    bankLine.Id));

    Assert.Contains(
        "completed reconciliation",
        exception.Message,
        StringComparison.OrdinalIgnoreCase);
}
}