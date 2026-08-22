using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class DimensionFinancialReportTests
{
    [Fact]
    public async Task ReportScope_IncludesOnlySelectedDivision()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var structures = new EnterpriseStructureService(test.Db);
        var nadi = await structures.AddBranchAsync(test.Organisation.Id, "NADI", "Nadi Branch");
        var retail = await structures.AddDivisionAsync(test.Organisation.Id, nadi.Id, "RETAIL", "Retail");
        var defaultBranch = await test.Db.Branches.AsNoTracking()
            .Include(x => x.Divisions)
            .SingleAsync(x => x.OrganisationId == test.Organisation.Id && x.IsDefault);
        var general = defaultBranch.Divisions.Single(x => x.IsDefault);
        var accounts = await test.Db.LedgerAccounts.AsNoTracking()
            .Where(x => x.OrganisationId == test.Organisation.Id)
            .ToListAsync();
        var bank = accounts.Single(x => x.Code == "1000");
        var revenue = accounts.Single(x => x.Code == "4000");

        await PostRevenue(test, defaultBranch.Id, general.Id, bank.Id, revenue.Id, 100m);
        await PostRevenue(test, nadi.Id, retail.Id, bank.Id, revenue.Id, 250m);

        var reports = new FinancialReportService(test.Db);
        var all = await reports.GetAsync(
            test.Organisation.Id,
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31));
        var scoped = await reports.GetAsync(
            test.Organisation.Id,
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31),
            divisionIds: [retail.Id]);

        Assert.Equal(350m, all.Balances.Single(x => x.Type == AccountType.Revenue).DisplayAmount);
        Assert.Equal(250m, scoped.Balances.Single(x => x.Type == AccountType.Revenue).DisplayAmount);
        Assert.Equal(250m, scoped.Balances.Single(x => x.Type == AccountType.Asset).DisplayAmount);
        Assert.Equal(250m, scoped.TrialBalance.Single(x => x.Code == "1000").Debit);
    }

    private static Task PostRevenue(
        AccountingTestDatabase test,
        Guid branchId,
        Guid divisionId,
        Guid bankId,
        Guid revenueId,
        decimal amount) =>
        test.Posting.PostAsync(
            test.UserId,
            new JournalPostRequest(
                test.Organisation.Id,
                new DateOnly(2026, 8, 23),
                $"REPORT-{Guid.NewGuid():N}",
                "Scoped reporting test",
                [
                    new JournalLineInput(bankId, "Bank", amount, 0m),
                    new JournalLineInput(revenueId, "Revenue", 0m, amount)
                ],
                branchId,
                divisionId));
}
