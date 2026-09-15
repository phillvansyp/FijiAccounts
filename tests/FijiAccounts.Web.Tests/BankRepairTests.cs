using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class BankRepairTests
{
    [Fact]
    public async Task DuplicateCodingRepairKeepsExistingPaymentAndRematchesStatement()
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        var bank = t.Account("1000");
        var date = new DateOnly(2026, 8, 18);
        var statement = new BankStatementLine { OrganisationId = t.Organisation.Id, BankAccountId = bank.Id,
            TransactionDate = date, Description = "Supplier payment", Amount = -100m };
        t.Db.BankStatementLines.Add(statement);
        await t.Db.SaveChangesAsync();
        var coding = await t.BankCoding.PostAndReconcileAsync(t.UserId,
            new(t.Organisation.Id, statement.Id, "6500", "Supplier payment", VatTreatment.OutOfScope));
        var payment = await t.Posting.PostAsync(t.UserId, new(t.Organisation.Id, date, "PAY", "Existing bill payment",
            [new(t.Account("2000").Id, "Payable", 100m, 0m), new(bank.Id, "Bank", 0m, 100m)]));
        var codingLine = coding.Lines.Single(x => x.LedgerAccountId == bank.Id).Id;
        var paymentLine = payment.Lines.Single(x => x.LedgerAccountId == bank.Id).Id;
        var service = new BankLedgerRepairService(t.Db, t.Access, t.Posting, t.Reconciliation,
            new BankReconciliationSessionService(t.Db, t.Access));
        var plan = new BankRepairPlan("duplicate-expense", t.Organisation.Id, bank.Id, t.UserId,
            [new(date, -200m, -100m)],
            [new("duplicate", coding.Id, true, date, "Remove duplicate expense")],
            [new(statement.Id, codingLine, paymentLine, null)]);
        await service.RunAsync(plan, true);
        var saved = await t.Db.BankStatementLines.AsNoTracking().SingleAsync(x => x.Id == statement.Id);
        Assert.Equal(paymentLine, saved.MatchedPostedJournalLineId);
        Assert.NotNull(saved.ReconciledAt);
        Assert.Equal(0m, await t.AccountBalanceAsync("6500"));
        Assert.Equal(-100m, await t.AccountBalanceAsync("1000"));
    }

    [Fact]
    public async Task ReversedEntryCannotBeMatchedOrReversedAgain()
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        var statement = new BankStatementLine { OrganisationId = t.Organisation.Id, BankAccountId = t.Account("1000").Id,
            TransactionDate = new(2026, 8, 18), Description = "Office", Amount = -100m };
        t.Db.BankStatementLines.Add(statement);
        await t.Db.SaveChangesAsync();
        var original = await t.BankCoding.PostAndReconcileAsync(t.UserId,
            new(t.Organisation.Id, statement.Id, "6500", "Office", VatTreatment.OutOfScope));
        var lineId = original.Lines.Single(x => x.LedgerAccountId == statement.BankAccountId).Id;
        await t.BankCoding.ReopenCodingAsync(t.UserId, t.Organisation.Id, statement.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            t.Reconciliation.ReconcileAsync(t.UserId, t.Organisation.Id, statement.Id, lineId));
        // Reproduce a legacy match to the old journal; the reopen guard must still reject it.
        t.Db.ChangeTracker.Clear();
        var legacy = await t.Db.BankStatementLines.SingleAsync(x => x.Id == statement.Id);
        legacy.MatchedPostedJournalLineId = lineId;
        legacy.ReconciledAt = DateTimeOffset.UtcNow;
        await t.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            t.BankCoding.ReopenCodingAsync(t.UserId, t.Organisation.Id, statement.Id));
        Assert.Equal(2, await t.Db.PostedJournals.CountAsync());
        Assert.Equal(0m, await t.AccountBalanceAsync("1000"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpeningDateRepairIsAtomicAndIdempotent(bool wrongExpectedBalance)
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        var bank = await t.BankAccounts.CreateAsync(t.UserId,
            new(t.Organisation.Id, "1010", "Bank", null, 100m, new(2026, 8, 24)));
        var source = await t.Db.PostedJournals.AsNoTracking().SingleAsync(x => x.Reference == "OPEN-1010");
        var service = new BankLedgerRepairService(t.Db, t.Access, t.Posting, t.Reconciliation,
            new BankReconciliationSessionService(t.Db, t.Access));
        var plan = new BankRepairPlan("test-opening", t.Organisation.Id, bank.Id, t.UserId,
            [new(new(2026, 1, 31), 0m, wrongExpectedBalance ? 999m : 100m), new(new(2026, 8, 31), 100m, 100m)],
            [new("remove-late", source.Id, true, source.EntryDate, "Cancel late opening entry"),
             new("opening", source.Id, false, new(2026, 1, 1), "Opening at conversion date")], []);
        if (wrongExpectedBalance)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(plan, true));
            t.Db.ChangeTracker.Clear();
            Assert.Equal(1, await t.Db.PostedJournals.CountAsync());
            return;
        }
        await service.RunAsync(plan, false);
        Assert.Equal(1, await t.Db.PostedJournals.CountAsync());
        await service.RunAsync(plan, true);
        Assert.Equal(3, await t.Db.PostedJournals.CountAsync());
        Assert.Contains("Already applied", await service.RunAsync(plan, true));
        Assert.Equal(3, await t.Db.PostedJournals.CountAsync());
        Assert.Equal(100m, await t.AccountBalanceAsync("1010"));
        Assert.Contains(source.Id, await BankCodingHistory.UnmatchableJournalIdsAsync(t.Db, t.Organisation.Id));
    }
}
