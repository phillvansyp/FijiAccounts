using System.Text.Json;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class GroupExchangeRateServiceHardeningTests
{
    [Fact]
    public async Task SetPresentationCurrencyAsync_NormalizesAuditsAndSuppressesNoOp()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new GroupExchangeRateService(test.Db);
        var group = await GroupAsync(test);

        await service.SetPresentationCurrencyAsync(
            test.UserId,
            test.Organisation.Id,
            " nzd ");
        await service.SetPresentationCurrencyAsync(
            test.UserId,
            test.Organisation.Id,
            "NZD");

        Assert.Equal(
            "NZD",
            await test.Db.OrganisationGroups
                .Where(x => x.Id == group.Id)
                .Select(x => x.PresentationCurrency)
                .SingleAsync());
        var audit = Assert.Single(await test.Db.AuditEvents.AsNoTracking().ToListAsync());
        Assert.Equal("OrganisationGroupPresentationCurrencyUpdated", audit.EventType);
        Assert.Equal(nameof(OrganisationGroup), audit.EntityType);
        Assert.Equal(group.Id.ToString(), audit.EntityId);
        Assert.Equal(test.Organisation.Id, audit.OrganisationId);
        Assert.Equal(test.UserId, audit.UserId);

        using var evidence = JsonDocument.Parse(audit.JsonData);
        Assert.Equal(group.Name, evidence.RootElement.GetProperty("GroupName").GetString());
        Assert.Equal("FJD", evidence.RootElement.GetProperty("OldCurrency").GetString());
        Assert.Equal("NZD", evidence.RootElement.GetProperty("NewCurrency").GetString());
    }

    [Fact]
    public async Task SaveAsync_CreatesUpdatesAuditsAndSuppressesUnchangedRate()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new GroupExchangeRateService(test.Db);
        var group = await GroupAsync(test);
        test.Organisation.BaseCurrency = "NZD";
        await test.Db.SaveChangesAsync();
        var request = new SaveGroupExchangeRateRequest(
            test.Organisation.Id,
            " nzd ",
            GroupExchangeRateType.Closing,
            new DateOnly(2026, 8, 31),
            1.5m);

        await service.SaveAsync(test.UserId, request);
        await service.SaveAsync(test.UserId, request with { FromCurrency = "NZD" });
        await service.SaveAsync(test.UserId, request with { Rate = 1.75m });

        var rate = Assert.Single(await test.Db.GroupExchangeRates.AsNoTracking().ToListAsync());
        Assert.Equal(1.75m, rate.Rate);
        var audits = await test.Db.AuditEvents
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .ToListAsync();
        Assert.Equal(
            ["GroupExchangeRateCreated", "GroupExchangeRateUpdated"],
            audits.Select(x => x.EventType));
        Assert.All(audits, audit =>
        {
            Assert.Equal(nameof(GroupExchangeRate), audit.EntityType);
            Assert.Equal(rate.Id.ToString(), audit.EntityId);
            Assert.Equal(test.Organisation.Id, audit.OrganisationId);
            Assert.Equal(test.UserId, audit.UserId);
        });

        using var created = JsonDocument.Parse(audits[0].JsonData);
        Assert.Equal(group.Name, created.RootElement.GetProperty("GroupName").GetString());
        Assert.Equal("NZD", created.RootElement.GetProperty("FromCurrency").GetString());
        Assert.Equal("FJD", created.RootElement.GetProperty("ToCurrency").GetString());
        Assert.Equal("Closing", created.RootElement.GetProperty("RateType").GetString());
        Assert.Equal("2026-08-31", created.RootElement.GetProperty("EffectiveDate").GetString());
        Assert.Equal(JsonValueKind.Null, created.RootElement.GetProperty("OldRate").ValueKind);
        Assert.Equal(1.5m, created.RootElement.GetProperty("NewRate").GetDecimal());

        using var updated = JsonDocument.Parse(audits[1].JsonData);
        Assert.Equal(1.5m, updated.RootElement.GetProperty("OldRate").GetDecimal());
        Assert.Equal(1.75m, updated.RootElement.GetProperty("NewRate").GetDecimal());
    }

    [Fact]
    public async Task ViewerAndUnrelatedUserCannotMutateOrCreateAuditNoise()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new GroupExchangeRateService(test.Db);
        var group = await GroupAsync(test);
        await test.Db.OrganisationGroupMemberships
            .Where(x => x.OrganisationGroupId == group.Id && x.UserId == test.UserId)
            .ExecuteUpdateAsync(update =>
                update.SetProperty(x => x.Role, OrganisationGroupRole.Viewer));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SetPresentationCurrencyAsync(test.UserId, test.Organisation.Id, "NZD"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SaveAsync(
                test.UserId,
                new(
                    test.Organisation.Id,
                    "NZD",
                    GroupExchangeRateType.Closing,
                    new DateOnly(2026, 8, 31),
                    1.5m)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SetPresentationCurrencyAsync(
                Guid.NewGuid().ToString(),
                test.Organisation.Id,
                "NZD"));

        Assert.Equal("FJD", (await GroupAsync(test)).PresentationCurrency);
        Assert.Empty(await test.Db.GroupExchangeRates.AsNoTracking().ToListAsync());
        Assert.Empty(await test.Db.AuditEvents.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task LegacyManagerOfEveryCompanyCanManageRates()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new GroupExchangeRateService(test.Db);
        test.Organisation.BaseCurrency = "NZD";
        var memberships = await test.Db.OrganisationGroupMemberships
            .Where(x => x.UserId == test.UserId)
            .ToListAsync();
        test.Db.OrganisationGroupMemberships.RemoveRange(memberships);
        await test.Db.SaveChangesAsync();

        await service.SaveAsync(test.UserId, RateRequest(test, "NZD"));

        Assert.Single(await test.Db.GroupExchangeRates.AsNoTracking().ToListAsync());
        Assert.Single(
            await test.Db.AuditEvents.AsNoTracking().ToListAsync(),
            x => x.EventType == "GroupExchangeRateCreated");
    }

    [Fact]
    public async Task InvalidMissingAndCrossGroupRequestsCreateNoAuditNoise()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new GroupExchangeRateService(test.Db);
        test.Organisation.BaseCurrency = "NZD";
        var missingGroupOrganisation = new Organisation
        {
            LegalName = "Standalone Without Group Limited",
            BaseCurrency = "NZD",
            Kind = OrganisationKind.Business
        };
        var foreignGroup = new OrganisationGroup
        {
            Name = "Foreign Group",
            PresentationCurrency = "USD"
        };
        var foreignCompany = new Organisation
        {
            LegalName = "Foreign Group Company Limited",
            BaseCurrency = "GBP",
            Kind = OrganisationKind.Business,
            OrganisationGroup = foreignGroup
        };
        test.Db.Organisations.AddRange(missingGroupOrganisation, foreignCompany);
        await test.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetPresentationCurrencyAsync(test.UserId, test.Organisation.Id, "XXX"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetPresentationCurrencyAsync(test.UserId, missingGroupOrganisation.Id, "NZD"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SetPresentationCurrencyAsync(test.UserId, foreignCompany.Id, "NZD"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveAsync(test.UserId, RateRequest(test, "GBP")));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveAsync(test.UserId, RateRequest(test, "FJD")));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveAsync(test.UserId, RateRequest(test, "NZD") with { Rate = 0m }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveAsync(test.UserId, RateRequest(test, "NZD") with
            {
                Type = (GroupExchangeRateType)999
            }));

        Assert.Equal("FJD", (await GroupAsync(test)).PresentationCurrency);
        Assert.Empty(await test.Db.GroupExchangeRates.AsNoTracking().ToListAsync());
        Assert.Empty(await test.Db.AuditEvents.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task GetAsyncScopesRatesToCurrentGroup()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new GroupExchangeRateService(test.Db);
        var group = await GroupAsync(test);
        test.Organisation.BaseCurrency = "NZD";
        var foreignGroup = new OrganisationGroup
        {
            Name = "Foreign Group",
            PresentationCurrency = "USD"
        };
        test.Db.GroupExchangeRates.AddRange(
            new GroupExchangeRate
            {
                OrganisationGroupId = group.Id,
                FromCurrency = "NZD",
                ToCurrency = "FJD",
                Type = GroupExchangeRateType.Closing,
                EffectiveDate = new DateOnly(2026, 8, 31),
                Rate = 1.5m
            },
            new GroupExchangeRate
            {
                OrganisationGroup = foreignGroup,
                FromCurrency = "GBP",
                ToCurrency = "USD",
                Type = GroupExchangeRateType.Closing,
                EffectiveDate = new DateOnly(2026, 8, 31),
                Rate = 1.25m
            });
        await test.Db.SaveChangesAsync();

        var configuration = await service.GetAsync(test.UserId, test.Organisation.Id);

        var rate = Assert.Single(configuration.Rates);
        Assert.Equal("NZD", rate.FromCurrency);
        Assert.Equal(group.Id, configuration.GroupId);
    }

    private static SaveGroupExchangeRateRequest RateRequest(
        AccountingTestDatabase test,
        string fromCurrency) =>
        new(
            test.Organisation.Id,
            fromCurrency,
            GroupExchangeRateType.Closing,
            new DateOnly(2026, 8, 31),
            1.5m);

    private static Task<OrganisationGroup> GroupAsync(AccountingTestDatabase test) =>
        test.Db.OrganisationGroups
            .AsNoTracking()
            .SingleAsync(x => x.Id == test.Organisation.OrganisationGroupId);
}
