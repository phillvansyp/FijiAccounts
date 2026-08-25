using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class GroupEliminationServiceTests
{
    [Fact]
    public async Task PostAsync_PostsBalancedImmutableJournalAndAuditEvent()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new GroupEliminationService(test.Db);

        var journal = await service.PostAsync(
            test.UserId,
            Request(test, 125m));

        var stored = await test.Db.GroupEliminationJournals
            .AsNoTracking()
            .Include(x => x.Lines)
            .SingleAsync(x => x.Id == journal.Id);
        Assert.Equal("ELIM-2026-001", stored.Reference);
        Assert.Equal("FJD", stored.Currency);
        Assert.Equal(125m, stored.Lines.Sum(x => x.Debit));
        Assert.Equal(125m, stored.Lines.Sum(x => x.Credit));
        Assert.Contains(
            await test.Db.AuditEvents.AsNoTracking().ToListAsync(),
            x => x.EventType == "GroupEliminationJournalPosted" &&
                 x.EntityId == journal.Id.ToString());

        var configuration = await service.GetAsync(test.UserId, test.Organisation.Id);
        Assert.True(configuration.CanManage);
        Assert.Contains(configuration.Accounts, x => x.Code == "4000");
        Assert.Single(configuration.Journals);
    }

    [Fact]
    public async Task PostAsync_RejectsUnbalancedJournalWithoutWritingAnything()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new GroupEliminationService(test.Db);
        var request = Request(test, 125m) with
        {
            Lines =
            [
                Line(test, "4000", debit: 125m),
                Line(test, "5000", credit: 124m)
            ]
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PostAsync(test.UserId, request));

        Assert.Contains("balance", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await test.Db.GroupEliminationJournals.AsNoTracking().ToListAsync());
        Assert.DoesNotContain(
            await test.Db.AuditEvents.AsNoTracking().ToListAsync(),
            x => x.EventType == "GroupEliminationJournalPosted");
    }

    [Fact]
    public async Task PostAsync_RejectsGroupViewer()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        await test.Db.OrganisationGroupMemberships
            .Where(x =>
                x.OrganisationGroupId == test.Organisation.OrganisationGroupId &&
                x.UserId == test.UserId)
            .ExecuteUpdateAsync(update =>
                update.SetProperty(x => x.Role, OrganisationGroupRole.Viewer));
        var service = new GroupEliminationService(test.Db);

        var configuration = await service.GetAsync(test.UserId, test.Organisation.Id);
        Assert.False(configuration.CanManage);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.PostAsync(test.UserId, Request(test, 125m)));
        Assert.Empty(await test.Db.GroupEliminationJournals.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task PostAsync_RejectsAccountOutsideGroupAndDuplicateReference()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new GroupEliminationService(test.Db);
        await service.PostAsync(test.UserId, Request(test, 125m));

        var duplicate = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PostAsync(test.UserId, Request(test, 125m)));
        Assert.Contains("already in use", duplicate.Message, StringComparison.OrdinalIgnoreCase);

        var invalid = Request(test, 125m) with
        {
            Reference = "ELIM-2026-002",
            Lines =
            [
                new("9999", "Outside account", AccountType.Revenue, "Invalid", 125m, 0m),
                Line(test, "5000", credit: 125m)
            ]
        };
        var outsideGroup = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PostAsync(test.UserId, invalid));
        Assert.Contains("not active", outsideGroup.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PostGroupEliminationRequest Request(
        AccountingTestDatabase test,
        decimal amount) =>
        new(
            test.Organisation.Id,
            new DateOnly(2026, 8, 25),
            "ELIM-2026-001",
            "Eliminate internal sale and cost",
            [
                Line(test, "4000", debit: amount),
                Line(test, "5000", credit: amount)
            ]);

    private static GroupEliminationLineInput Line(
        AccountingTestDatabase test,
        string code,
        decimal debit = 0m,
        decimal credit = 0m)
    {
        var account = test.Account(code);
        return new(
            account.Code,
            account.Name,
            account.Type,
            "Intercompany elimination",
            debit,
            credit);
    }
}
