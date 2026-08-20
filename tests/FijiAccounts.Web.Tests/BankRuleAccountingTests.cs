using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class BankRuleAccountingTests
{
    [Fact]
    public async Task ApplyAsync_WhenStoredTargetAccountBecomesBankAccount_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var target =
            test.Account("6500");

        var service =
            new BankRuleService(
                test.Db,
                test.Access,
                test.Posting,
                test.Reconciliation);

        var rule =
            await service.CreateAsync(
                test.UserId,
                new BankRuleRequest(
                    OrganisationId: test.Organisation.Id,
                    Name: "Office expense",
                    DescriptionContains: "OFFICE",
                    Direction: BankRuleDirection.MoneyOut,
                    TargetAccountId: target.Id));

        var bank =
            test.Account("1000");

        var line =
    new BankStatementLine
    {
        OrganisationId = test.Organisation.Id,
        BankAccountId = bank.Id,
        TransactionDate = new DateOnly(2026, 8, 20),
        Description = "OFFICE SUPPLIES",
        Reference = "RULE-DRIFT-001",
        Amount = -100m,
        Source = "Test"
    };

test.Db.BankStatementLines.Add(line);
await test.Db.SaveChangesAsync();

        target.IsBankAccount = true;
        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.ApplyAsync(
                        test.UserId,
                        test.Organisation.Id,
                        rule.Id,
                        line.Id));

        Assert.Contains(
            "target",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());

        var reloadedLine =
            await test.Db.BankStatementLines
                .AsNoTracking()
                .SingleAsync(x => x.Id == line.Id);

        Assert.Null(reloadedLine.ReconciledAt);
    }
}