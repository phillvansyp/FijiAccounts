using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class BankOpeningBalanceAccountingTests
{
    [Fact]
    public async Task PositiveOpeningBalance_DebitsBankAndCreditsOpeningEquity()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank =
            await test.BankAccounts.CreateAsync(
                test.UserId,
                new CreateBankAccountRequest(
                    test.Organisation.Id,
                    "1010",
                    "Operating Bank",
                    "12345678",
                    50000m,
                    new DateOnly(2026, 7, 1)));

        var journal =
            await test.Db.PostedJournals
                .AsNoTracking()
                .Include(x => x.Lines)
                .ThenInclude(x => x.LedgerAccount)
                .SingleAsync(x =>
                    x.OrganisationId ==
                        test.Organisation.Id &&
                    x.Reference == "OPEN-1010");

        Assert.Equal(
            new DateOnly(2026, 7, 1),
            journal.EntryDate);

        Assert.Equal(2, journal.Lines.Count);

        var bankLine =
            journal.Lines.Single(x =>
                x.LedgerAccountId == bank.Id);

        var equityLine =
            journal.Lines.Single(x =>
                x.LedgerAccount.Code == "3200");

        Assert.Equal(50000m, bankLine.Debit);
        Assert.Equal(0m, bankLine.Credit);

        Assert.Equal(0m, equityLine.Debit);
        Assert.Equal(50000m, equityLine.Credit);

        Assert.Equal(
            journal.Lines.Sum(x => x.Debit),
            journal.Lines.Sum(x => x.Credit));

        Assert.Equal(
            50000m,
            await test.AccountBalanceAsync("1010"));

        Assert.Equal(
            -50000m,
            await test.AccountBalanceAsync("3200"));
    }

    [Fact]
    public async Task NegativeOpeningBalance_CreditsBankAndDebitsOpeningEquity()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank =
            await test.BankAccounts.CreateAsync(
                test.UserId,
                new CreateBankAccountRequest(
                    test.Organisation.Id,
                    "1010",
                    "Overdrawn Bank",
                    null,
                    -7500m,
                    new DateOnly(2026, 7, 1)));

        var journal =
            await test.Db.PostedJournals
                .AsNoTracking()
                .Include(x => x.Lines)
                .ThenInclude(x => x.LedgerAccount)
                .SingleAsync(x =>
                    x.OrganisationId ==
                        test.Organisation.Id &&
                    x.Reference == "OPEN-1010");

        var bankLine =
            journal.Lines.Single(x =>
                x.LedgerAccountId == bank.Id);

        var equityLine =
            journal.Lines.Single(x =>
                x.LedgerAccount.Code == "3200");

        Assert.Equal(0m, bankLine.Debit);
        Assert.Equal(7500m, bankLine.Credit);

        Assert.Equal(7500m, equityLine.Debit);
        Assert.Equal(0m, equityLine.Credit);

        Assert.Equal(
            journal.Lines.Sum(x => x.Debit),
            journal.Lines.Sum(x => x.Credit));

        Assert.Equal(
            -7500m,
            await test.AccountBalanceAsync("1010"));

        Assert.Equal(
            7500m,
            await test.AccountBalanceAsync("3200"));
    }

    [Fact]
    public async Task ZeroOpeningBalance_CreatesBankWithoutOpeningJournal()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank =
            await test.BankAccounts.CreateAsync(
                test.UserId,
                new CreateBankAccountRequest(
                    test.Organisation.Id,
                    "1010",
                    "Zero Balance Bank",
                    null,
                    0m,
                    new DateOnly(2026, 7, 1)));

        Assert.True(bank.IsBankAccount);

        Assert.False(
            await test.Db.PostedJournals
                .AnyAsync(x =>
                    x.OrganisationId ==
                        test.Organisation.Id &&
                    x.Reference == "OPEN-1010"));

        Assert.Equal(
            0m,
            await test.AccountBalanceAsync("1010"));

        Assert.Equal(
            0m,
            await test.AccountBalanceAsync("3200"));
    }

    [Fact]
    public async Task OpeningBalanceJournal_PreservesAccountingEquation()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        await test.BankAccounts.CreateAsync(
            test.UserId,
            new CreateBankAccountRequest(
                test.Organisation.Id,
                "1010",
                "Conversion Bank",
                null,
                25000m,
                new DateOnly(2026, 7, 1)));

        var lines =
            await test.Db.PostedJournalLines
                .AsNoTracking()
                .Include(x => x.LedgerAccount)
                .Where(x =>
                    x.PostedJournal.OrganisationId ==
                        test.Organisation.Id)
                .ToListAsync();

        var totalDebits =
            lines.Sum(x => x.Debit);

        var totalCredits =
            lines.Sum(x => x.Credit);

        Assert.Equal(
            totalDebits,
            totalCredits);

        Assert.Equal(25000m, totalDebits);
        Assert.Equal(25000m, totalCredits);
    }
}