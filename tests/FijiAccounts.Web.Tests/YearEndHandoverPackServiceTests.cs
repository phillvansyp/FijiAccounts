using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class YearEndHandoverPackServiceTests
{
    [Fact]
    public async Task LockedPeriod_ExportsSelfVerifyingAccountingBundle()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        await test.Posting.PostAsync(
            test.UserId,
            new JournalPostRequest(
                test.Organisation.Id,
                new DateOnly(2026, 7, 31),
                "YEAR-END-ADJ",
                "Approved year-end adjustment",
                [
                    new JournalLineInput(
                        test.Account("6000").Id,
                        "Year-end expense",
                        250m,
                        0m),
                    new JournalLineInput(
                        test.Account("2000").Id,
                        "Year-end accrual",
                        0m,
                        250m)
                ]));
        var period = await CreatePeriodAsync(test, locked: false);
        var periodService = new AccountingPeriodService(test.Db, test.Access);
        await periodService.SetLockedAsync(
            test.UserId,
            test.Organisation.Id,
            period.Id,
            true);
        var service = CreateService(test);

        var pack = await service.CreateAsync(
            test.UserId,
            test.Organisation.Id,
            period.Id);

        Assert.Equal(
            "account-island-handover-20260701-20260731.zip",
            pack.FileName);
        using var stream = new MemoryStream(pack.Content);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var expected = new[]
        {
            "manifest.json",
            "trial-balance.csv",
            "profit-and-loss.csv",
            "balance-sheet.csv",
            "general-ledger.csv",
            "vat-workpaper.csv",
            "bank-reconciliations.csv",
            "period-control-audit.csv"
        };
        Assert.Equal(expected, archive.Entries.Select(x => x.FullName));

        using var manifest = JsonDocument.Parse(await ReadAsync(archive, "manifest.json"));
        var root = manifest.RootElement;
        Assert.Equal(test.Organisation.Id, root.GetProperty("OrganisationId").GetGuid());
        Assert.Equal(period.Id, root.GetProperty("PeriodId").GetGuid());
        Assert.Equal("FJD", root.GetProperty("Currency").GetString());
        Assert.NotEqual(JsonValueKind.Null, root.GetProperty("LockedAt").ValueKind);

        foreach (var file in root.GetProperty("Files").EnumerateArray())
        {
            var name = file.GetProperty("Name").GetString()!;
            var content = await ReadBytesAsync(archive, name);
            var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            Assert.Equal(file.GetProperty("Sha256").GetString(), hash);
        }

        var ledger = await ReadAsync(archive, "general-ledger.csv");
        Assert.Contains("YEAR-END-ADJ", ledger);
        Assert.Contains("250.00", ledger);
        var controlAudit = await ReadAsync(archive, "period-control-audit.csv");
        Assert.Contains("AccountingPeriodLocked", controlAudit);
        Assert.True(await test.Db.AuditEvents.AnyAsync(x =>
            x.OrganisationId == test.Organisation.Id &&
            x.EntityId == period.Id.ToString() &&
            x.EventType == "YearEndHandoverPackExported"));
    }

    [Fact]
    public async Task UnlockedPeriod_CannotBeExportedAsFinalPack()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var period = await CreatePeriodAsync(test, locked: false);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(test).CreateAsync(
                test.UserId,
                test.Organisation.Id,
                period.Id));

        Assert.Equal(
            "Lock the accounting period before exporting its final handover pack.",
            error.Message);
    }

    [Fact]
    public async Task NonManager_CannotExportHandoverPack()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var period = await CreatePeriodAsync(test, locked: true);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateService(test).CreateAsync(
                "not-a-member",
                test.Organisation.Id,
                period.Id));
    }

    private static YearEndHandoverPackService CreateService(
        AccountingTestDatabase test) =>
        new(
            test.Db,
            test.Access,
            new FinancialReportService(test.Db),
            new VatWorkpaperService(test.Db));

    private static async Task<AccountingPeriod> CreatePeriodAsync(
        AccountingTestDatabase test,
        bool locked)
    {
        var period = new AccountingPeriod
        {
            OrganisationId = test.Organisation.Id,
            Name = "July 2026",
            StartsOn = new DateOnly(2026, 7, 1),
            EndsOn = new DateOnly(2026, 7, 31),
            IsLocked = locked,
            LockedAt = locked ? DateTimeOffset.UtcNow : null,
            LockedByUserId = locked ? test.UserId : null
        };
        test.Db.AccountingPeriods.Add(period);
        await test.Db.SaveChangesAsync();
        return period;
    }

    private static async Task<string> ReadAsync(
        ZipArchive archive,
        string name) =>
        Encoding.UTF8.GetString(await ReadBytesAsync(archive, name));

    private static async Task<byte[]> ReadBytesAsync(
        ZipArchive archive,
        string name)
    {
        var entry = archive.GetEntry(name)
            ?? throw new InvalidOperationException($"Missing ZIP entry {name}.");
        await using var source = entry.Open();
        using var output = new MemoryStream();
        await source.CopyToAsync(output);
        return output.ToArray();
    }
}
