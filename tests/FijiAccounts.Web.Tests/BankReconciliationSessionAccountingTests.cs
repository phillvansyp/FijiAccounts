using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class BankReconciliationSessionAccountingTests
{
    [Fact]
    public async Task CreateSession_CalculatesLedgerBalanceAndDifference()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank = test.Account("1000");

        await test.Posting.PostAsync(
            test.UserId,
            new JournalPostRequest(
                test.Organisation.Id,
                new DateOnly(2026, 8, 10),
                "BANK-OPEN-001",
                "Opening bank balance",
                [
                    new(
                        bank.Id,
                        "Opening bank balance",
                        1000m,
                        0m),
                    new(
                        test.Account("3000").Id,
                        "Opening equity",
                        0m,
                        1000m)
                ]));

        var service =
            new BankReconciliationSessionService(
                test.Db,
                test.Access);

        var session =
            await service.CreateAsync(
                test.UserId,
                new BankReconciliationSessionRequest(
                    test.Organisation.Id,
                    bank.Id,
                    new DateOnly(2026, 8, 1),
                    new DateOnly(2026, 8, 31),
                    0m,
                    1050m));

        Assert.Equal(1000m, session.LedgerBalance);
        Assert.Equal(50m, session.Difference);
        Assert.False(session.IsCompleted);
    }

    [Fact]
    public async Task CreateSession_RejectsOverlappingPeriod()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank = test.Account("1000");

        var service =
            new BankReconciliationSessionService(
                test.Db,
                test.Access);

        await service.CreateAsync(
            test.UserId,
            new BankReconciliationSessionRequest(
                test.Organisation.Id,
                bank.Id,
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 31),
                0m,
                0m));

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new BankReconciliationSessionRequest(
                            test.Organisation.Id,
                            bank.Id,
                            new DateOnly(2026, 8, 15),
                            new DateOnly(2026, 9, 15),
                            0m,
                            0m)));

        Assert.Contains(
            "already exists",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteSession_RejectsNonZeroDifference()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank = test.Account("1000");

        await test.Posting.PostAsync(
            test.UserId,
            new JournalPostRequest(
                test.Organisation.Id,
                new DateOnly(2026, 8, 10),
                "BANK-DIFF-001",
                "Bank deposit",
                [
                    new(
                        bank.Id,
                        "Bank deposit",
                        500m,
                        0m),
                    new(
                        test.Account("3000").Id,
                        "Equity",
                        0m,
                        500m)
                ]));

        var service =
            new BankReconciliationSessionService(
                test.Db,
                test.Access);

        var session =
            await service.CreateAsync(
                test.UserId,
                new BankReconciliationSessionRequest(
                    test.Organisation.Id,
                    bank.Id,
                    new DateOnly(2026, 8, 1),
                    new DateOnly(2026, 8, 31),
                    0m,
                    550m));

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CompleteAsync(
                        test.UserId,
                        test.Organisation.Id,
                        session.Id));

        Assert.Contains(
            "difference must be zero",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.False(session.IsCompleted);
    }

    [Fact]
    public async Task CompleteSession_RejectsUnreconciledStatementLines()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank = test.Account("1000");

        var statement =
            new BankStatementLine
            {
                OrganisationId =
                    test.Organisation.Id,
                BankAccountId =
                    bank.Id,
                TransactionDate =
                    new DateOnly(2026, 8, 12),
                Description =
                    "Unreconciled deposit",
                Reference =
                    "STMT-001",
                Amount =
                    100m,
                Source =
                    "Test"
            };

        test.Db.BankStatementLines.Add(statement);

        await test.Db.SaveChangesAsync();

        var service =
            new BankReconciliationSessionService(
                test.Db,
                test.Access);

        var session =
            await service.CreateAsync(
                test.UserId,
                new BankReconciliationSessionRequest(
                    test.Organisation.Id,
                    bank.Id,
                    new DateOnly(2026, 8, 1),
                    new DateOnly(2026, 8, 31),
                    0m,
                    0m));

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CompleteAsync(
                        test.UserId,
                        test.Organisation.Id,
                        session.Id));

        Assert.Contains(
            "all statement lines",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.False(session.IsCompleted);
    }

    [Fact]
    public async Task CompleteSession_CompletesBalancedReconciliation()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank = test.Account("1000");

        await test.Posting.PostAsync(
            test.UserId,
            new JournalPostRequest(
                test.Organisation.Id,
                new DateOnly(2026, 8, 10),
                "BANK-COMPLETE-001",
                "Opening bank balance",
                [
                    new(
                        bank.Id,
                        "Opening bank balance",
                        750m,
                        0m),
                    new(
                        test.Account("3000").Id,
                        "Opening equity",
                        0m,
                        750m)
                ]));

        var service =
            new BankReconciliationSessionService(
                test.Db,
                test.Access);

        var session =
            await service.CreateAsync(
                test.UserId,
                new BankReconciliationSessionRequest(
                    test.Organisation.Id,
                    bank.Id,
                    new DateOnly(2026, 8, 1),
                    new DateOnly(2026, 8, 31),
                    0m,
                    750m));

        var completed =
            await service.CompleteAsync(
                test.UserId,
                test.Organisation.Id,
                session.Id);

        Assert.True(completed.IsCompleted);
        Assert.Equal(0m, completed.Difference);
        Assert.Equal(750m, completed.LedgerBalance);
        Assert.NotNull(completed.CompletedAt);
        Assert.Equal(
            test.UserId,
            completed.CompletedByUserId);

        var persisted =
            await test.Db.BankReconciliationSessions
                .AsNoTracking()
                .SingleAsync(x => x.Id == session.Id);

        Assert.True(persisted.IsCompleted);
        Assert.NotNull(persisted.CompletedAt);
    }

    [Fact]
    public async Task RefreshSession_RejectsCompletedReconciliation()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank = test.Account("1000");

        var service =
            new BankReconciliationSessionService(
                test.Db,
                test.Access);

        var session =
            await service.CreateAsync(
                test.UserId,
                new BankReconciliationSessionRequest(
                    test.Organisation.Id,
                    bank.Id,
                    new DateOnly(2026, 8, 1),
                    new DateOnly(2026, 8, 31),
                    0m,
                    0m));

        await service.CompleteAsync(
            test.UserId,
            test.Organisation.Id,
            session.Id);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.RefreshAsync(
                        test.UserId,
                        test.Organisation.Id,
                        session.Id));

        Assert.Contains(
            "completed reconciliation cannot be changed",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
public async Task RefreshSession_RecalculatesLedgerBalanceAndDifference()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var bank = test.Account("1000");

    var service =
        new BankReconciliationSessionService(
            test.Db,
            test.Access);

    var session =
        await service.CreateAsync(
            test.UserId,
            new BankReconciliationSessionRequest(
                test.Organisation.Id,
                bank.Id,
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 31),
                0m,
                1000m));

    Assert.Equal(0m, session.LedgerBalance);
    Assert.Equal(1000m, session.Difference);

    await test.Posting.PostAsync(
        test.UserId,
        new JournalPostRequest(
            test.Organisation.Id,
            new DateOnly(2026, 8, 15),
            "BANK-REFRESH-001",
            "Deposit after reconciliation started",
            [
                new(
                    bank.Id,
                    "Bank deposit",
                    600m,
                    0m),
                new(
                    test.Account("3000").Id,
                    "Equity",
                    0m,
                    600m)
            ]));

    var refreshed =
        await service.RefreshAsync(
            test.UserId,
            test.Organisation.Id,
            session.Id);

    Assert.Equal(600m, refreshed.LedgerBalance);
    Assert.Equal(400m, refreshed.Difference);

    var persisted =
        await test.Db.BankReconciliationSessions
            .AsNoTracking()
            .SingleAsync(x => x.Id == session.Id);

    Assert.Equal(600m, persisted.LedgerBalance);
    Assert.Equal(400m, persisted.Difference);
}
}
