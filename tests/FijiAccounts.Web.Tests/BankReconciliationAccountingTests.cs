using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class BankReconciliationAccountingTests
{
    [Fact]
    public async Task Reconcile_MatchingDeposit_ReconcilesStatementLine()
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
                    new DateOnly(2026, 8, 18),
                    "BANK-DEP-001",
                    "Bank deposit",
                    [
                        new(
                            bank.Id,
                            "Bank deposit",
                            500m,
                            0m),
                        new(
                            income.Id,
                            "Bank deposit",
                            0m,
                            500m)
                    ]));

        var statement =
            await test.Reconciliation.AddStatementLineAsync(
                test.UserId,
                new StatementLineRequest(
                    test.Organisation.Id,
                    bank.Id,
                    new DateOnly(2026, 8, 18),
                    "Deposit",
                    "BANK-DEP-001",
                    500m));

        var bankLine =
            await test.Db.PostedJournalLines
                .SingleAsync(x =>
                    x.PostedJournalId == journal.Id &&
                    x.LedgerAccountId == bank.Id);

        await test.Reconciliation.ReconcileAsync(
            test.UserId,
            test.Organisation.Id,
            statement.Id,
            bankLine.Id);

        var reconciled =
            await test.Db.BankStatementLines
                .AsNoTracking()
                .SingleAsync(x => x.Id == statement.Id);

        Assert.Equal(
            bankLine.Id,
            reconciled.MatchedPostedJournalLineId);

        Assert.NotNull(reconciled.ReconciledAt);

        Assert.Equal(
            test.UserId,
            reconciled.ReconciledByUserId);
    }

    [Fact]
    public async Task Reconcile_MatchingWithdrawal_ReconcilesStatementLine()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank = test.Account("1000");
        var expense = test.Account("6400");

        var journal =
            await test.Posting.PostAsync(
                test.UserId,
                new JournalPostRequest(
                    test.Organisation.Id,
                    new DateOnly(2026, 8, 18),
                    "BANK-FEE-001",
                    "Bank fee",
                    [
                        new(
                            expense.Id,
                            "Bank fee",
                            25m,
                            0m),
                        new(
                            bank.Id,
                            "Bank fee",
                            0m,
                            25m)
                    ]));

        var statement =
            await test.Reconciliation.AddStatementLineAsync(
                test.UserId,
                new StatementLineRequest(
                    test.Organisation.Id,
                    bank.Id,
                    new DateOnly(2026, 8, 18),
                    "Bank fee",
                    "BANK-FEE-001",
                    -25m));

        var bankLine =
            await test.Db.PostedJournalLines
                .SingleAsync(x =>
                    x.PostedJournalId == journal.Id &&
                    x.LedgerAccountId == bank.Id);

        await test.Reconciliation.ReconcileAsync(
            test.UserId,
            test.Organisation.Id,
            statement.Id,
            bankLine.Id);

        var reconciled =
            await test.Db.BankStatementLines
                .AsNoTracking()
                .SingleAsync(x => x.Id == statement.Id);

        Assert.Equal(
            bankLine.Id,
            reconciled.MatchedPostedJournalLineId);

        Assert.NotNull(reconciled.ReconciledAt);
    }

    [Fact]
    public async Task Reconcile_AmountMismatch_IsRejected()
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
                    new DateOnly(2026, 8, 18),
                    "BANK-MISMATCH",
                    "Bank deposit",
                    [
                        new(
                            bank.Id,
                            "Bank deposit",
                            500m,
                            0m),
                        new(
                            income.Id,
                            "Bank deposit",
                            0m,
                            500m)
                    ]));

        var statement =
            await test.Reconciliation.AddStatementLineAsync(
                test.UserId,
                new StatementLineRequest(
                    test.Organisation.Id,
                    bank.Id,
                    new DateOnly(2026, 8, 18),
                    "Deposit",
                    null,
                    499m));

        var bankLine =
            await test.Db.PostedJournalLines
                .SingleAsync(x =>
                    x.PostedJournalId == journal.Id &&
                    x.LedgerAccountId == bank.Id);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => test.Reconciliation.ReconcileAsync(
                    test.UserId,
                    test.Organisation.Id,
                    statement.Id,
                    bankLine.Id));

        Assert.Contains(
            "do not match",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        var unchanged =
            await test.Db.BankStatementLines
                .AsNoTracking()
                .SingleAsync(x => x.Id == statement.Id);

        Assert.Null(unchanged.MatchedPostedJournalLineId);
        Assert.Null(unchanged.ReconciledAt);
    }

    [Fact]
    public async Task Reconcile_NonBankLedgerLine_IsRejected()
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
                    new DateOnly(2026, 8, 18),
                    "WRONG-LINE",
                    "Bank deposit",
                    [
                        new(
                            bank.Id,
                            "Bank deposit",
                            500m,
                            0m),
                        new(
                            income.Id,
                            "Bank deposit",
                            0m,
                            500m)
                    ]));

        var statement =
            await test.Reconciliation.AddStatementLineAsync(
                test.UserId,
                new StatementLineRequest(
                    test.Organisation.Id,
                    bank.Id,
                    new DateOnly(2026, 8, 18),
                    "Deposit",
                    null,
                    500m));

        var incomeLine =
            await test.Db.PostedJournalLines
                .SingleAsync(x =>
                    x.PostedJournalId == journal.Id &&
                    x.LedgerAccountId == income.Id);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => test.Reconciliation.ReconcileAsync(
                    test.UserId,
                    test.Organisation.Id,
                    statement.Id,
                    incomeLine.Id));

        Assert.Contains(
            "Matching bank ledger entry not found",
            exception.Message);
    }

    [Fact]
    public async Task Reconcile_StatementLineTwice_IsRejected()
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
                    new DateOnly(2026, 8, 18),
                    "DOUBLE-STMT",
                    "Bank deposit",
                    [
                        new(
                            bank.Id,
                            "Bank deposit",
                            100m,
                            0m),
                        new(
                            income.Id,
                            "Bank deposit",
                            0m,
                            100m)
                    ]));

        var statement =
            await test.Reconciliation.AddStatementLineAsync(
                test.UserId,
                new StatementLineRequest(
                    test.Organisation.Id,
                    bank.Id,
                    new DateOnly(2026, 8, 18),
                    "Deposit",
                    null,
                    100m));

        var bankLine =
            await test.Db.PostedJournalLines
                .SingleAsync(x =>
                    x.PostedJournalId == journal.Id &&
                    x.LedgerAccountId == bank.Id);

        await test.Reconciliation.ReconcileAsync(
            test.UserId,
            test.Organisation.Id,
            statement.Id,
            bankLine.Id);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => test.Reconciliation.ReconcileAsync(
                    test.UserId,
                    test.Organisation.Id,
                    statement.Id,
                    bankLine.Id));

        Assert.Contains(
            "already reconciled",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reconcile_LedgerLineCannotBeUsedForTwoStatements()
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
                    new DateOnly(2026, 8, 18),
                    "DOUBLE-LEDGER",
                    "Bank deposit",
                    [
                        new(
                            bank.Id,
                            "Bank deposit",
                            200m,
                            0m),
                        new(
                            income.Id,
                            "Bank deposit",
                            0m,
                            200m)
                    ]));

        var first =
            await test.Reconciliation.AddStatementLineAsync(
                test.UserId,
                new StatementLineRequest(
                    test.Organisation.Id,
                    bank.Id,
                    new DateOnly(2026, 8, 18),
                    "Deposit one",
                    null,
                    200m));

        var second =
            await test.Reconciliation.AddStatementLineAsync(
                test.UserId,
                new StatementLineRequest(
                    test.Organisation.Id,
                    bank.Id,
                    new DateOnly(2026, 8, 18),
                    "Deposit two",
                    null,
                    200m));

        var bankLine =
            await test.Db.PostedJournalLines
                .SingleAsync(x =>
                    x.PostedJournalId == journal.Id &&
                    x.LedgerAccountId == bank.Id);

        await test.Reconciliation.ReconcileAsync(
            test.UserId,
            test.Organisation.Id,
            first.Id,
            bankLine.Id);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => test.Reconciliation.ReconcileAsync(
                    test.UserId,
                    test.Organisation.Id,
                    second.Id,
                    bankLine.Id));

        Assert.Contains(
            "already reconciled",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        var secondReloaded =
            await test.Db.BankStatementLines
                .AsNoTracking()
                .SingleAsync(x => x.Id == second.Id);

        Assert.Null(secondReloaded.MatchedPostedJournalLineId);
        Assert.Null(secondReloaded.ReconciledAt);
    }
}