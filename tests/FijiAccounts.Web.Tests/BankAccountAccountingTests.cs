using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Services;
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
}