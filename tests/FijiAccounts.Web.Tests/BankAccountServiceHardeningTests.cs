using System.Text.Json;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class BankAccountServiceHardeningTests
{
    [Fact]
    public async Task CreateAsync_NormalizesAndAuditsMetadataWithoutFullAccountNumber()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();

        var bank = await test.BankAccounts.CreateAsync(
            test.UserId,
            Request(test) with
            {
                Code = " 1010 ",
                Name = " Operating Bank ",
                AccountNumber = " 12345678 "
            });

        Assert.Equal("1010", bank.Code);
        Assert.Equal("Operating Bank", bank.Name);
        Assert.Equal("12345678", bank.BankAccountNumber);
        var audit = await test.Db.AuditEvents
            .AsNoTracking()
            .SingleAsync(x => x.EventType == "BankAccountCreated");
        Assert.Equal(test.Organisation.Id, audit.OrganisationId);
        Assert.Equal(test.UserId, audit.UserId);
        Assert.Equal(nameof(LedgerAccount), audit.EntityType);
        Assert.Equal(bank.Id.ToString(), audit.EntityId);
        Assert.DoesNotContain("12345678", audit.JsonData, StringComparison.Ordinal);

        using var evidence = JsonDocument.Parse(audit.JsonData);
        Assert.Equal("1010", evidence.RootElement.GetProperty("Code").GetString());
        Assert.True(evidence.RootElement.GetProperty("HasAccountNumber").GetBoolean());
        Assert.Equal("5678", evidence.RootElement.GetProperty("AccountNumberLast4").GetString());
        Assert.Equal("Asset", evidence.RootElement.GetProperty("Type").GetString());
        Assert.Equal(0m, evidence.RootElement.GetProperty("OpeningBalance").GetDecimal());
        Assert.Equal(JsonValueKind.Null, evidence.RootElement.GetProperty("OpeningJournalId").ValueKind);
    }

    [Fact]
    public async Task CreateAsync_WithOpeningBalance_LinksAuditToJournal()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();

        var bank = await test.BankAccounts.CreateAsync(
            test.UserId,
            Request(test) with { OpeningBalance = 250m });

        var journal = await test.Db.PostedJournals
            .AsNoTracking()
            .SingleAsync(x => x.Reference == "OPEN-1010");
        var audit = await test.Db.AuditEvents
            .AsNoTracking()
            .SingleAsync(x =>
                x.EventType == "BankAccountCreated" &&
                x.EntityId == bank.Id.ToString());
        using var evidence = JsonDocument.Parse(audit.JsonData);
        Assert.Equal(
            journal.Id.ToString(),
            evidence.RootElement.GetProperty("OpeningJournalId").GetString());
        Assert.Equal(250m, evidence.RootElement.GetProperty("OpeningBalance").GetDecimal());
    }

    [Fact]
    public async Task InvalidAndUnauthorizedRequests_CreateNoAccountOrAudit()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var initialAccountCount = await test.Db.LedgerAccounts.CountAsync();
        var valid = Request(test);
        var invalidRequests = new[]
        {
            valid with { Code = " " },
            valid with { Code = new string('C', 21) },
            valid with { Name = " " },
            valid with { Name = new string('N', 161) },
            valid with { AccountNumber = new string('1', 81) }
        };

        foreach (var request in invalidRequests)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                test.BankAccounts.CreateAsync(test.UserId, request));
        }

        await test.Db.OrganisationMemberships
            .Where(x =>
                x.UserId == test.UserId &&
                x.OrganisationId == test.Organisation.Id)
            .ExecuteUpdateAsync(update =>
                update.SetProperty(x => x.Role, OrganisationRole.ReadOnly));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            test.BankAccounts.CreateAsync(test.UserId, valid));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            test.BankAccounts.CreateAsync(
                test.UserId,
                valid with { OrganisationId = Guid.NewGuid() }));

        Assert.Equal(initialAccountCount, await test.Db.LedgerAccounts.CountAsync());
        Assert.Empty(await test.Db.AuditEvents.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task OpeningJournalFailure_RollsBackAccountAndCreationAudit()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Db.AccountingPeriods.Add(new AccountingPeriod
        {
            OrganisationId = test.Organisation.Id,
            Name = "Locked period",
            StartsOn = new DateOnly(2026, 7, 1),
            EndsOn = new DateOnly(2026, 7, 31),
            IsLocked = true
        });
        await test.Db.SaveChangesAsync();
        var initialAccountCount = await test.Db.LedgerAccounts.CountAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            test.BankAccounts.CreateAsync(
                test.UserId,
                Request(test) with { OpeningBalance = 250m }));

        Assert.Equal(initialAccountCount, await test.Db.LedgerAccounts.CountAsync());
        Assert.False(await test.Db.AuditEvents
            .AnyAsync(x => x.EventType == "BankAccountCreated"));
        Assert.False(await test.Db.PostedJournals
            .AnyAsync(x => x.Reference == "OPEN-1010"));
    }

    private static CreateBankAccountRequest Request(AccountingTestDatabase test) =>
        new(
            test.Organisation.Id,
            "1010",
            "Operating Bank",
            null,
            0m,
            new DateOnly(2026, 7, 1));
}
