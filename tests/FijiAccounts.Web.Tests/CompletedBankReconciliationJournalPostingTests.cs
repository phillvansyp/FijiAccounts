using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class CompletedBankReconciliationJournalPostingTests
{
    [Fact]
    public async Task PostAsync_RejectsBankJournalInsideCompletedReconciliation()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank = test.Account("1000");
        var expense = test.Account("6500");

        bank.BankAccountKind =
            BankAccountKind.DebitCard;

        await test.Db.SaveChangesAsync();

        test.Db.BankReconciliationSessions.Add(
            new BankReconciliationSession
            {
                OrganisationId = test.Organisation.Id,
                BankAccountId = bank.Id,
                StatementStartDate = new DateOnly(2026, 8, 1),
                StatementEndDate = new DateOnly(2026, 8, 31),
                IsCompleted = true,
                CreatedByUserId = test.UserId
            });

        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    test.Posting.PostAsync(
                        test.UserId,
                        new JournalPostRequest(
                            OrganisationId: test.Organisation.Id,
                            Date: new DateOnly(2026, 8, 18),
                            Reference: "MANUAL-BANK-001",
                            Description: "Manual bank correction",
                            Lines:
                            [
                                new JournalLineInput(
                                    expense.Id,
                                    "Manual bank correction",
                                    100m,
                                    0m),

                                new JournalLineInput(
                                    bank.Id,
                                    "Manual bank correction",
                                    0m,
                                    100m)
                            ])));

        Assert.Equal(
            "A journal cannot post to a bank account inside a completed reconciliation period.",
            ex.Message);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());
    }

    [Fact]
public async Task PostAsync_AllowsNonBankJournalInsideCompletedReconciliation()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var bank = test.Account("1000");
    var expense = test.Account("6500");
    var payable = test.Account("2000");

    bank.BankAccountKind =
        BankAccountKind.DebitCard;

    await test.Db.SaveChangesAsync();

    test.Db.BankReconciliationSessions.Add(
        new BankReconciliationSession
        {
            OrganisationId = test.Organisation.Id,
            BankAccountId = bank.Id,
            StatementStartDate = new DateOnly(2026, 8, 1),
            StatementEndDate = new DateOnly(2026, 8, 31),
            IsCompleted = true,
            CreatedByUserId = test.UserId
        });

    await test.Db.SaveChangesAsync();

    var journal =
        await test.Posting.PostAsync(
            test.UserId,
            new JournalPostRequest(
                OrganisationId: test.Organisation.Id,
                Date: new DateOnly(2026, 8, 18),
                Reference: "MANUAL-NONBANK-001",
                Description: "Non-bank adjustment",
                Lines:
                [
                    new JournalLineInput(
                        expense.Id,
                        "Non-bank adjustment",
                        100m,
                        0m),

                    new JournalLineInput(
                        payable.Id,
                        "Non-bank adjustment",
                        0m,
                        100m)
                ]));

    Assert.NotEqual(Guid.Empty, journal.Id);
}

[Fact]
public async Task PostAsync_AllowsBankJournalOutsideCompletedReconciliation()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var bank = test.Account("1000");
    var expense = test.Account("6500");

    bank.BankAccountKind =
        BankAccountKind.DebitCard;

    await test.Db.SaveChangesAsync();

    test.Db.BankReconciliationSessions.Add(
        new BankReconciliationSession
        {
            OrganisationId = test.Organisation.Id,
            BankAccountId = bank.Id,
            StatementStartDate = new DateOnly(2026, 8, 1),
            StatementEndDate = new DateOnly(2026, 8, 31),
            IsCompleted = true,
            CreatedByUserId = test.UserId
        });

    await test.Db.SaveChangesAsync();

    var journal =
        await test.Posting.PostAsync(
            test.UserId,
            new JournalPostRequest(
                OrganisationId: test.Organisation.Id,
                Date: new DateOnly(2026, 9, 5),
                Reference: "MANUAL-BANK-002",
                Description: "September bank correction",
                Lines:
                [
                    new JournalLineInput(
                        expense.Id,
                        "September bank correction",
                        100m,
                        0m),

                    new JournalLineInput(
                        bank.Id,
                        "September bank correction",
                        0m,
                        100m)
                ]));

    Assert.NotEqual(Guid.Empty, journal.Id);
}
}