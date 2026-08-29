using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class GroupAccountMappingServiceTests
{
    [Fact]
    public async Task InitialiseFromCompanyAsync_CreatesAnIdempotentCanonicalChartAndMappings()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new GroupAccountMappingService(test.Db);
        var activeAccountCount = await test.Db.LedgerAccounts
            .CountAsync(x => x.OrganisationId == test.Organisation.Id && x.IsActive);

        var created = await service.InitialiseFromCompanyAsync(
            test.UserId,
            test.Organisation.Id,
            test.Organisation.Id);
        var repeated = await service.InitialiseFromCompanyAsync(
            test.UserId,
            test.Organisation.Id,
            test.Organisation.Id);
        var setup = await service.GetAsync(test.UserId, test.Organisation.Id);

        Assert.Equal(activeAccountCount, created);
        Assert.Equal(0, repeated);
        Assert.Equal(activeAccountCount, setup.GroupAccounts.Count);
        Assert.Equal(activeAccountCount, setup.Mappings.Count);
        Assert.True(setup.MappingComplete);
        Assert.Single(
            await test.Db.AuditEvents.AsNoTracking()
                .Where(x => x.EventType == "GroupChartInitialisedFromCompany")
                .ToListAsync());
    }

    [Fact]
    public async Task SetMappingAsync_RequiresMatchingAccountTypesAndManagerAccess()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new GroupAccountMappingService(test.Db);
        var sales = test.Account("4000");
        var assetGroupAccount = await service.CreateAsync(
            test.UserId,
            new(test.Organisation.Id, "GROUP-ASSET", "Group asset", AccountType.Asset));

        var mismatch = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetMappingAsync(
                test.UserId,
                new(test.Organisation.Id, sales.Id, assetGroupAccount.Id)));
        Assert.Contains("same type", mismatch.Message, StringComparison.OrdinalIgnoreCase);

        await test.Db.OrganisationGroupMemberships
            .Where(x => x.OrganisationGroupId == test.Organisation.OrganisationGroupId &&
                        x.UserId == test.UserId)
            .ExecuteUpdateAsync(update =>
                update.SetProperty(x => x.Role, OrganisationGroupRole.Viewer));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CreateAsync(
                test.UserId,
                new(test.Organisation.Id, "GROUP-REV", "Group revenue", AccountType.Revenue)));
        Assert.Empty(await test.Db.GroupLedgerAccountMappings.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task SetMappingAsync_UsesCanonicalAccountInConsolidatedReports()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var mappings = new GroupAccountMappingService(test.Db);
        var sales = test.Account("4000");
        var canonical = await mappings.CreateAsync(
            test.UserId,
            new(test.Organisation.Id, "GRP-4100", "Group trading revenue", AccountType.Revenue));
        await mappings.SetMappingAsync(
            test.UserId,
            new(test.Organisation.Id, sales.Id, canonical.Id));
        await PostRevenue(test, 175m);

        var report = await new GroupFinancialReportService(
                test.Db,
                new FinancialReportService(test.Db),
                test.Access)
            .GetAsync(
                test.UserId,
                test.Organisation.Id,
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 31));

        var revenue = Assert.Single(
            report.Consolidated.Balances,
            x => x.Type == AccountType.Revenue);
        Assert.Equal("GRP-4100", revenue.Code);
        Assert.Equal("Group trading revenue", revenue.Name);
        Assert.Equal(175m, revenue.DisplayAmount);
    }

    [Fact]
    public async Task SaveIntercompanyConfigurationAsync_ValidatesAndUpsertsDirectionalAccounts()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var second = await new EnterpriseStructureService(test.Db).AddCompanyAsync(
            test.UserId,
            new(
                test.Organisation.Id,
                "Second Fiji Trading Limited",
                null,
                null,
                "FJ",
                OrganisationKind.Business));
        var service = new GroupAccountMappingService(test.Db);
        var receivable = test.Account("1100");
        var payable = test.Account("2000");
        var revenue = test.Account("4000");
        var expense = test.Account("5000");
        var request = new SaveIntercompanyAccountConfigurationRequest(
            test.Organisation.Id,
            test.Organisation.Id,
            second.Id,
            receivable.Id,
            payable.Id,
            revenue.Id,
            expense.Id);

        await service.SaveIntercompanyConfigurationAsync(test.UserId, request);
        await service.SaveIntercompanyConfigurationAsync(test.UserId, request);

        var configuration = Assert.Single(
            await test.Db.IntercompanyAccountConfigurations.AsNoTracking().ToListAsync());
        Assert.Equal(second.Id, configuration.CounterpartyOrganisationId);
        Assert.Equal(receivable.Id, configuration.ReceivableAccountId);
        Assert.Contains(
            await test.Db.AuditEvents.AsNoTracking().ToListAsync(),
            x => x.EventType == "IntercompanyAccountConfigurationUpdated");

        var wrongType = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveIntercompanyConfigurationAsync(
                test.UserId,
                request with { ReceivableAccountId = revenue.Id, RevenueAccountId = receivable.Id }));
        Assert.Contains("asset", wrongType.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task PostRevenue(AccountingTestDatabase test, decimal amount)
    {
        await test.Posting.PostAsync(
            test.UserId,
            new(
                test.Organisation.Id,
                new DateOnly(2026, 8, 20),
                "GROUP-MAPPING-REVENUE",
                "Revenue for group mapping test",
                [
                    new(test.Account("1000").Id, "Cash received", amount, 0m),
                    new(test.Account("4000").Id, "Trading revenue", 0m, amount)
                ]));
    }
}
