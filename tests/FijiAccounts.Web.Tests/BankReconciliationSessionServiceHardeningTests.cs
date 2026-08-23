using System.Text.Json;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class BankReconciliationSessionServiceHardeningTests
{
    [Fact]
    public async Task CreateAndCompleteAsync_RecordOldAndNewAuditEvidence()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new BankReconciliationSessionService(test.Db, test.Access);
        var session = await service.CreateAsync(test.UserId, Request(test));

        await service.CompleteAsync(
            test.UserId,
            test.Organisation.Id,
            session.Id);

        var audits = await AuditsAsync(test, session.Id);
        Assert.Equal(
            [
                "BankReconciliationSessionCreated",
                "BankReconciliationSessionCompleted"
            ],
            audits.Select(x => x.EventType));
        Assert.All(audits, audit =>
        {
            Assert.Equal(test.UserId, audit.UserId);
            Assert.Equal(nameof(BankReconciliationSession), audit.EntityType);
        });

        using var completion = JsonDocument.Parse(audits[1].JsonData);
        Assert.False(completion.RootElement.GetProperty("Old").GetProperty("IsCompleted").GetBoolean());
        Assert.True(completion.RootElement.GetProperty("New").GetProperty("IsCompleted").GetBoolean());
        Assert.Equal(
            test.UserId,
            completion.RootElement.GetProperty("New").GetProperty("CompletedByUserId").GetString());
    }

    [Fact]
    public async Task RefreshAsync_SuppressesNoOpAndAuditsChangedBalances()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new BankReconciliationSessionService(test.Db, test.Access);
        var session = await service.CreateAsync(
            test.UserId,
            Request(test) with { ClosingStatementBalance = 100m });

        await service.RefreshAsync(test.UserId, test.Organisation.Id, session.Id);
        Assert.Single(await AuditsAsync(test, session.Id));

        await test.Posting.PostAsync(
            test.UserId,
            new JournalPostRequest(
                test.Organisation.Id,
                new DateOnly(2026, 8, 15),
                "RECON-REFRESH",
                "Reconciliation refresh",
                [
                    new(test.Account("1000").Id, "Bank", 60m, 0m),
                    new(test.Account("3000").Id, "Equity", 0m, 60m)
                ]));

        await service.RefreshAsync(test.UserId, test.Organisation.Id, session.Id);

        var refresh = Assert.Single(
            await AuditsAsync(test, session.Id),
            x => x.EventType == "BankReconciliationSessionRefreshed");
        using var evidence = JsonDocument.Parse(refresh.JsonData);
        Assert.Equal(0m, evidence.RootElement.GetProperty("Old").GetProperty("LedgerBalance").GetDecimal());
        Assert.Equal(60m, evidence.RootElement.GetProperty("New").GetProperty("LedgerBalance").GetDecimal());
        Assert.Equal(40m, evidence.RootElement.GetProperty("New").GetProperty("Difference").GetDecimal());
    }

    [Fact]
    public async Task FailedCompletion_DoesNotLeakRecalculationOrAudit()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new BankReconciliationSessionService(test.Db, test.Access);
        var session = await service.CreateAsync(test.UserId, Request(test));
        await test.Posting.PostAsync(
            test.UserId,
            new JournalPostRequest(
                test.Organisation.Id,
                new DateOnly(2026, 8, 15),
                "RECON-DIFFERENCE",
                "Unmatched movement",
                [
                    new(test.Account("1000").Id, "Bank", 100m, 0m),
                    new(test.Account("3000").Id, "Equity", 0m, 100m)
                ]));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CompleteAsync(test.UserId, test.Organisation.Id, session.Id));

        Assert.Equal(0m, session.LedgerBalance);
        Assert.Equal(0m, session.Difference);
        Assert.False(session.IsCompleted);
        var stored = await test.Db.BankReconciliationSessions
            .AsNoTracking()
            .SingleAsync(x => x.Id == session.Id);
        Assert.Equal(0m, stored.LedgerBalance);
        Assert.Equal(0m, stored.Difference);
        Assert.DoesNotContain(
            await AuditsAsync(test, session.Id),
            x => x.EventType == "BankReconciliationSessionCompleted");
    }

    [Fact]
    public async Task UnauthorizedAndCrossTenantAttempts_CreateNoAuditNoise()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new BankReconciliationSessionService(test.Db, test.Access);
        var session = await service.CreateAsync(test.UserId, Request(test));
        var otherOrganisation = new Organisation
        {
            LegalName = "Other Organisation Limited",
            Kind = OrganisationKind.Business
        };
        test.Db.Organisations.Add(otherOrganisation);
        test.Db.OrganisationMemberships.Add(new OrganisationMembership
        {
            OrganisationId = otherOrganisation.Id,
            Organisation = otherOrganisation,
            UserId = test.UserId,
            Role = OrganisationRole.Owner
        });
        await test.Db.SaveChangesAsync();
        var initialAuditCount = await test.Db.AuditEvents.CountAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RefreshAsync(test.UserId, otherOrganisation.Id, session.Id));
        await test.Db.OrganisationMemberships
            .Where(x =>
                x.UserId == test.UserId &&
                x.OrganisationId == test.Organisation.Id)
            .ExecuteUpdateAsync(update =>
                update.SetProperty(x => x.Role, OrganisationRole.ReadOnly));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CreateAsync(test.UserId, Request(test)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.RefreshAsync(test.UserId, test.Organisation.Id, session.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CompleteAsync(test.UserId, test.Organisation.Id, session.Id));

        Assert.Equal(initialAuditCount, await test.Db.AuditEvents.CountAsync());
    }

    private static BankReconciliationSessionRequest Request(AccountingTestDatabase test) =>
        new(
            test.Organisation.Id,
            test.Account("1000").Id,
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31),
            0m,
            0m);

    private static Task<List<AuditEvent>> AuditsAsync(
        AccountingTestDatabase test,
        Guid sessionId) =>
        test.Db.AuditEvents
            .AsNoTracking()
            .Where(x =>
                x.EntityType == nameof(BankReconciliationSession) &&
                x.EntityId == sessionId.ToString())
            .OrderBy(x => x.Id)
            .ToListAsync();
}
