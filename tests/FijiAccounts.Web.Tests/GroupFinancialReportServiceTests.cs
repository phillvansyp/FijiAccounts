using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class GroupFinancialReportServiceTests
{
    [Fact]
    public async Task GetAsync_AllowsLegacyOwnerWhoManagesEveryCompany()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var groupMemberships = await test.Db.OrganisationGroupMemberships
            .Where(x => x.UserId == test.UserId)
            .ToListAsync();
        test.Db.OrganisationGroupMemberships.RemoveRange(groupMemberships);
        await test.Db.SaveChangesAsync();
        var service = new GroupFinancialReportService(
            test.Db,
            new FinancialReportService(test.Db),
            test.Access);

        var result = await service.GetAsync(
            test.UserId,
            test.Organisation.Id,
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31));

        Assert.Single(result.Companies);
        Assert.Equal(test.Organisation.Id, result.Companies[0].OrganisationId);
    }

    [Fact]
    public async Task GetAsync_ConsolidatesCompanyLedgersAndPreservesBreakdown()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var structures = new EnterpriseStructureService(test.Db);
        var second = await structures.AddCompanyAsync(
            test.UserId,
            new CreateGroupCompanyRequest(
                test.Organisation.Id,
                "Second Trading Limited",
                null,
                null,
                "FJ",
                OrganisationKind.Business));
        await PostRevenue(test, test.Organisation.Id, 100m);
        await PostRevenue(test, second.Id, 250m);
        var service = new GroupFinancialReportService(
            test.Db,
            new FinancialReportService(test.Db),
            test.Access);

        var result = await service.GetAsync(
            test.UserId,
            test.Organisation.Id,
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31));

        Assert.Equal("FJD", result.Currency);
        Assert.Equal(2, result.Companies.Count);
        Assert.Equal(350m, result.Consolidated.Balances.Single(x => x.Type == AccountType.Revenue).DisplayAmount);
        Assert.Equal(100m, result.Companies.Single(x => x.OrganisationId == test.Organisation.Id).NetProfit);
        Assert.Equal(250m, result.Companies.Single(x => x.OrganisationId == second.Id).NetProfit);

        second.BaseCurrency = "NZD";
        await test.Db.SaveChangesAsync();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetAsync(test.UserId, test.Organisation.Id, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)));
        Assert.Contains("exchange rates", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task PostRevenue(AccountingTestDatabase test, Guid organisationId, decimal amount)
    {
        var accounts = await test.Db.LedgerAccounts.AsNoTracking().Where(x => x.OrganisationId == organisationId).ToListAsync();
        await test.Posting.PostAsync(
            test.UserId,
            new JournalPostRequest(
                organisationId,
                new DateOnly(2026, 8, 23),
                $"GROUP-{Guid.NewGuid():N}",
                "Group report test",
                [
                    new JournalLineInput(accounts.Single(x => x.Code == "1000").Id, "Bank", amount, 0m),
                    new JournalLineInput(accounts.Single(x => x.Code == "4000").Id, "Revenue", 0m, amount)
                ]));
    }
}
