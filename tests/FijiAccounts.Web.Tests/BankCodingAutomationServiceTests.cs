using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class BankCodingAutomationServiceTests
{
    [Fact]
    public async Task ApplyAsync_PostsAndReconcilesEveryExactUnreconciledMatch()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var memoryService = new BankCodingMemoryService(test.Db, test.Access);
        var automation = new BankCodingAutomationService(
            test.Db,
            test.Access,
            test.BankCoding,
            new BankReconciliationSessionService(test.Db, test.Access));
        var division = await test.Db.Divisions.SingleAsync();
        var bank = test.Account("1000");

        var first = await test.Reconciliation.AddStatementLineAsync(
            test.UserId,
            new(test.Organisation.Id, bank.Id, new DateOnly(2026, 1, 31), "PAYMENT TO 9808313648", null, -8156m));
        var second = await test.Reconciliation.AddStatementLineAsync(
            test.UserId,
            new(test.Organisation.Id, bank.Id, new DateOnly(2026, 2, 28), "  payment   to 9808313648 ", null, -8156m));
        var otherAmount = await test.Reconciliation.AddStatementLineAsync(
            test.UserId,
            new(test.Organisation.Id, bank.Id, new DateOnly(2026, 3, 31), "PAYMENT TO 9808313648", null, -8155m));
        var otherPayee = await test.Reconciliation.AddStatementLineAsync(
            test.UserId,
            new(test.Organisation.Id, bank.Id, new DateOnly(2026, 4, 30), "ANOTHER PAYEE", null, -8156m));

        var memory = await memoryService.RememberAsync(
            test.UserId,
            new(
                test.Organisation.Id,
                "PAYMENT TO 9808313648",
                -8156m,
                test.Account("2500").Id,
                division.Id,
                "Loan repayment",
                VatTreatment.OutOfScope));

        var result = await automation.ApplyAsync(test.UserId, test.Organisation.Id, memory.Id);

        Assert.Equal(2, result.Applied);
        Assert.Equal(0, result.NeedsReview);
        Assert.NotNull((await test.Db.BankStatementLines.AsNoTracking().SingleAsync(x => x.Id == first.Id)).ReconciledAt);
        Assert.NotNull((await test.Db.BankStatementLines.AsNoTracking().SingleAsync(x => x.Id == second.Id)).ReconciledAt);
        Assert.Null((await test.Db.BankStatementLines.AsNoTracking().SingleAsync(x => x.Id == otherAmount.Id)).ReconciledAt);
        Assert.Null((await test.Db.BankStatementLines.AsNoTracking().SingleAsync(x => x.Id == otherPayee.Id)).ReconciledAt);
        Assert.Equal(2, await test.Db.AuditEvents.CountAsync(x => x.EventType == "RememberedBankCodingApplied"));
    }

    [Fact]
    public async Task ApplyAllAsync_LeavesConflictingLedgerMatchForReview()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var memoryService = new BankCodingMemoryService(test.Db, test.Access);
        var automation = new BankCodingAutomationService(
            test.Db,
            test.Access,
            test.BankCoding,
            new BankReconciliationSessionService(test.Db, test.Access));
        var division = await test.Db.Divisions.SingleAsync();
        var bank = test.Account("1000");
        var date = new DateOnly(2026, 5, 10);

        await test.Posting.PostAsync(
            test.UserId,
            new(
                test.Organisation.Id,
                date,
                "EXISTING-PAYMENT",
                "Existing supplier payment",
                [
                    new(test.Account("6500").Id, "Existing expense", 5m, 0m),
                    new(bank.Id, "Existing payment", 0m, 5m)
                ]));
        var statement = await test.Reconciliation.AddStatementLineAsync(
            test.UserId,
            new(test.Organisation.Id, bank.Id, date, "MAINTENANCE FEE", null, -5m));
        await memoryService.RememberAsync(
            test.UserId,
            new(
                test.Organisation.Id,
                "MAINTENANCE FEE",
                -5m,
                test.Account("6400").Id,
                division.Id,
                "Bank maintenance fee",
                VatTreatment.OutOfScope));

        var result = await automation.ApplyAllAsync(test.UserId, test.Organisation.Id);

        Assert.Equal(0, result.Applied);
        Assert.Equal(1, result.NeedsReview);
        Assert.Null((await test.Db.BankStatementLines.AsNoTracking().SingleAsync(x => x.Id == statement.Id)).ReconciledAt);
    }
}
