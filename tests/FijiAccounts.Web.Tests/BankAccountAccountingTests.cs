using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Services;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class BankAccountAccountingTests
{
    [Fact]
    public async Task CreateAsync_WhenOpeningBalanceEquityHasWrongType_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var openingEquity = test.Account("3200");
        openingEquity.Type = AccountType.Asset;

        await test.Db.SaveChangesAsync();

        var accountCountBefore =
            await test.Db.LedgerAccounts.CountAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var service =
            new BankAccountService(
                test.Db,
                test.Access,
                test.Posting);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new CreateBankAccountRequest(
                            OrganisationId: test.Organisation.Id,
                            Code: "1010",
                            Name: "Secondary Bank",
                            AccountNumber: "12345678",
                            OpeningBalance: 100m,
                            OpeningBalanceDate:
                                new DateOnly(2026, 8, 20))));

        Assert.Contains(
            "3200",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            accountCountBefore,
            await test.Db.LedgerAccounts.CountAsync());

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());
    }

        [Fact]
    public async Task CreateAsync_WhenOpeningBalanceDateIsInsideLockedAccountingPeriod_RollsBackBankAccount()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        test.Db.AccountingPeriods.Add(
            new AccountingPeriod
            {
                OrganisationId = test.Organisation.Id,
                Name = "August 2026",
                StartsOn = new DateOnly(2026, 8, 1),
                EndsOn = new DateOnly(2026, 8, 31),
                IsLocked = true
            });

        await test.Db.SaveChangesAsync();

        var accountCountBefore =
            await test.Db.LedgerAccounts.CountAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var service =
            new BankAccountService(
                test.Db,
                test.Access,
                test.Posting);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new CreateBankAccountRequest(
                            OrganisationId: test.Organisation.Id,
                            Code: "1010",
                            Name: "Locked Opening Bank",
                            AccountNumber: "12345678",
                            OpeningBalance: 100m,
                            OpeningBalanceDate:
                                new DateOnly(2026, 8, 20))));

        Assert.Contains(
            "locked",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            accountCountBefore,
            await test.Db.LedgerAccounts.CountAsync());

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());

        Assert.False(
            await test.Db.LedgerAccounts
                .AsNoTracking()
                .AnyAsync(x =>
                    x.OrganisationId == test.Organisation.Id &&
                    x.Code == "1010"));
    }
}
