using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class BankReconciliationUnmatchTests
{
    [Fact]
    public async Task UnreconcileAsync_RemovesMatchAndWritesAuditEvent()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var bank = test.Account("1000");
        var date = new DateOnly(2026, 6, 17);
        var statement = await test.Reconciliation.AddStatementLineAsync(
            test.UserId,
            new StatementLineRequest(
                test.Organisation.Id,
                bank.Id,
                date,
                "Supplier payment",
                "SUP-001",
                -100m));
        var journal = await test.Posting.PostAsync(
            test.UserId,
            new JournalPostRequest(
                test.Organisation.Id,
                date,
                "SUP-001",
                "Supplier payment",
                [
                    new JournalLineInput(test.Account("2000").Id, "Supplier payment", 100m, 0m),
                    new JournalLineInput(bank.Id, "Supplier payment", 0m, 100m)
                ]));
        var bankLine = journal.Lines.Single(x => x.LedgerAccountId == bank.Id);
        await test.Reconciliation.ReconcileAsync(
            test.UserId,
            test.Organisation.Id,
            statement.Id,
            bankLine.Id);

        await test.Reconciliation.UnreconcileAsync(
            test.UserId,
            test.Organisation.Id,
            statement.Id,
            "Matched to the wrong payment date");

        var reloaded = await test.Db.BankStatementLines.AsNoTracking().SingleAsync(x => x.Id == statement.Id);
        Assert.Null(reloaded.MatchedPostedJournalLineId);
        Assert.Null(reloaded.ReconciledAt);
        Assert.Null(reloaded.ReconciledByUserId);
        Assert.True(await test.Db.AuditEvents.AnyAsync(x =>
            x.EntityId == statement.Id.ToString() &&
            x.EventType == "BankStatementLineUnreconciled" &&
            x.JsonData.Contains("Matched to the wrong payment date")));
    }
}
