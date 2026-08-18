using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class AccountingPeriodLockAccountingTests
{
    [Fact]
    public async Task PostingInsideLockedPeriod_IsRejectedAndPersistsNothing()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var period =
            new AccountingPeriod
            {
                OrganisationId = test.Organisation.Id,
                Name = "July 2026",
                StartsOn = new DateOnly(2026, 7, 1),
                EndsOn = new DateOnly(2026, 7, 31),
                IsLocked = true,
                LockedAt = DateTimeOffset.UtcNow,
                LockedByUserId = test.UserId
            };

        test.Db.AccountingPeriods.Add(period);

        await test.Db.SaveChangesAsync();

        var before =
            await test.Db.PostedJournals.CountAsync();

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    test.Posting.PostAsync(
                        test.UserId,
                        new JournalPostRequest(
                            test.Organisation.Id,
                            new DateOnly(2026, 7, 15),
                            "LOCK-TEST-001",
                            "Locked period test",
                            [
                                new(
                                    test.Account("1000").Id,
                                    "Locked period test",
                                    100m,
                                    0m),

                                new(
                                    test.Account("4000").Id,
                                    "Locked period test",
                                    0m,
                                    100m)
                            ])));

        Assert.Equal(
            "The accounting period is locked.",
            ex.Message);

        var after =
            await test.Db.PostedJournals.CountAsync();

        Assert.Equal(before, after);

        Assert.False(
            await test.Db.PostedJournals.AnyAsync(x =>
                x.Reference == "LOCK-TEST-001"));
    }

    [Fact]
    public async Task PostingOnLockedPeriodStartDate_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        test.Db.AccountingPeriods.Add(
            new AccountingPeriod
            {
                OrganisationId = test.Organisation.Id,
                Name = "July 2026",
                StartsOn = new DateOnly(2026, 7, 1),
                EndsOn = new DateOnly(2026, 7, 31),
                IsLocked = true,
                LockedAt = DateTimeOffset.UtcNow,
                LockedByUserId = test.UserId
            });

        await test.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                test.Posting.PostAsync(
                    test.UserId,
                    new JournalPostRequest(
                        test.Organisation.Id,
                        new DateOnly(2026, 7, 1),
                        "LOCK-START",
                        "Locked start boundary",
                        [
                            new(
                                test.Account("1000").Id,
                                "Locked start boundary",
                                50m,
                                0m),

                            new(
                                test.Account("4000").Id,
                                "Locked start boundary",
                                0m,
                                50m)
                        ])));
    }

    [Fact]
    public async Task PostingOnLockedPeriodEndDate_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        test.Db.AccountingPeriods.Add(
            new AccountingPeriod
            {
                OrganisationId = test.Organisation.Id,
                Name = "July 2026",
                StartsOn = new DateOnly(2026, 7, 1),
                EndsOn = new DateOnly(2026, 7, 31),
                IsLocked = true,
                LockedAt = DateTimeOffset.UtcNow,
                LockedByUserId = test.UserId
            });

        await test.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                test.Posting.PostAsync(
                    test.UserId,
                    new JournalPostRequest(
                        test.Organisation.Id,
                        new DateOnly(2026, 7, 31),
                        "LOCK-END",
                        "Locked end boundary",
                        [
                            new(
                                test.Account("1000").Id,
                                "Locked end boundary",
                                50m,
                                0m),

                            new(
                                test.Account("4000").Id,
                                "Locked end boundary",
                                0m,
                                50m)
                        ])));
    }

    [Fact]
    public async Task PostingOutsideLockedPeriod_Succeeds()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        test.Db.AccountingPeriods.Add(
            new AccountingPeriod
            {
                OrganisationId = test.Organisation.Id,
                Name = "July 2026",
                StartsOn = new DateOnly(2026, 7, 1),
                EndsOn = new DateOnly(2026, 7, 31),
                IsLocked = true,
                LockedAt = DateTimeOffset.UtcNow,
                LockedByUserId = test.UserId
            });

        await test.Db.SaveChangesAsync();

        var journal =
            await test.Posting.PostAsync(
                test.UserId,
                new JournalPostRequest(
                    test.Organisation.Id,
                    new DateOnly(2026, 8, 1),
                    "OUTSIDE-LOCK",
                    "Outside locked period",
                    [
                        new(
                            test.Account("1000").Id,
                            "Outside locked period",
                            100m,
                            0m),

                        new(
                            test.Account("4000").Id,
                            "Outside locked period",
                            0m,
                            100m)
                    ]));

        Assert.NotEqual(Guid.Empty, journal.Id);

        Assert.True(
            await test.Db.PostedJournals.AnyAsync(x =>
                x.Reference == "OUTSIDE-LOCK"));
    }

    [Fact]
    public async Task PostingInsideUnlockedPeriod_Succeeds()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        test.Db.AccountingPeriods.Add(
            new AccountingPeriod
            {
                OrganisationId = test.Organisation.Id,
                Name = "July 2026",
                StartsOn = new DateOnly(2026, 7, 1),
                EndsOn = new DateOnly(2026, 7, 31),
                IsLocked = false
            });

        await test.Db.SaveChangesAsync();

        var journal =
            await test.Posting.PostAsync(
                test.UserId,
                new JournalPostRequest(
                    test.Organisation.Id,
                    new DateOnly(2026, 7, 15),
                    "UNLOCKED-TEST",
                    "Unlocked period test",
                    [
                        new(
                            test.Account("1000").Id,
                            "Unlocked period test",
                            75m,
                            0m),

                        new(
                            test.Account("4000").Id,
                            "Unlocked period test",
                            0m,
                            75m)
                    ]));

        Assert.NotEqual(Guid.Empty, journal.Id);

        Assert.True(
            await test.Db.PostedJournals.AnyAsync(x =>
                x.Reference == "UNLOCKED-TEST"));
    }
}