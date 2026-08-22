using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record GroupCompanyFinancialSummary(
    Guid OrganisationId,
    string LegalName,
    string Currency,
    decimal Revenue,
    decimal Expenses,
    decimal NetProfit,
    decimal Assets,
    decimal Liabilities,
    decimal Equity);

public sealed record GroupFinancialReportData(
    Guid GroupId,
    string GroupName,
    string Currency,
    IReadOnlyList<GroupCompanyFinancialSummary> Companies,
    FinancialReportData Consolidated);

public sealed class GroupFinancialReportService(
    ApplicationDbContext db,
    FinancialReportService financialReports,
    TenantAccessService tenantAccess)
{
    public async Task<GroupFinancialReportData> GetAsync(
        string userId,
        Guid currentOrganisationId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var group = await db.OrganisationGroups
            .AsNoTracking()
            .Where(x => x.Companies.Any(company => company.Id == currentOrganisationId))
            .Select(x => new
            {
                OrganisationGroupId = x.Id,
                x.Name,
                Companies = x.Companies.OrderBy(company => company.LegalName).ToList()
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("This organisation does not belong to an organisation group.");

        var hasGroupMembership = await db.OrganisationGroupMemberships
            .AsNoTracking()
            .AnyAsync(
                x => x.OrganisationGroupId == group.OrganisationGroupId && x.UserId == userId,
                cancellationToken);
        if (!hasGroupMembership)
        {
            var managedCompanyIds = await db.OrganisationMemberships
                .AsNoTracking()
                .Where(x =>
                    x.UserId == userId &&
                    x.Organisation.OrganisationGroupId == group.OrganisationGroupId &&
                    (x.Role == OrganisationRole.Owner || x.Role == OrganisationRole.Administrator))
                .Select(x => x.OrganisationId)
                .ToListAsync(cancellationToken);

            if (group.Companies.Any(company => !managedCompanyIds.Contains(company.Id)))
            {
                throw new UnauthorizedAccessException("You do not have access to this organisation group.");
            }
        }

        var accessibleIds = (await tenantAccess.ListAsync(userId)).Select(x => x.Organisation.Id).ToHashSet();
        var inaccessible = group.Companies.Where(x => !accessibleIds.Contains(x.Id)).Select(x => x.LegalName).ToArray();
        if (inaccessible.Length > 0)
        {
            throw new UnauthorizedAccessException(
                $"Consolidation requires ledger access to every company. Missing access: {string.Join(", ", inaccessible)}.");
        }

        foreach (var company in group.Companies)
        {
            if (await tenantAccess.GetReportDivisionScopeAsync(userId, company.Id, cancellationToken) is not null)
            {
                throw new UnauthorizedAccessException(
                    $"Consolidation requires full branch and division access to {company.LegalName}.");
            }
        }

        var currencies = group.Companies.Select(x => x.BaseCurrency).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (currencies.Length != 1)
        {
            throw new InvalidOperationException(
                "This group contains multiple base currencies. Configure group exchange rates before consolidating them.");
        }

        var companyReports = new List<(Organisation Company, FinancialReportData Report)>();
        foreach (var company in group.Companies)
        {
            companyReports.Add((company, await financialReports.GetAsync(company.Id, from, to, cancellationToken)));
        }

        var balances = companyReports.SelectMany(x => x.Report.Balances)
            .GroupBy(x => new { x.Code, x.Name, x.Type })
            .Select(x => new FinancialAccountBalance(x.Key.Code, x.Key.Name, x.Key.Type, x.Sum(y => y.DisplayAmount)))
            .OrderBy(x => x.Code)
            .ToList();
        var trial = companyReports.SelectMany(x => x.Report.TrialBalance)
            .GroupBy(x => new { x.Code, x.Name })
            .Select(x => new TrialBalanceRow(x.Key.Code, x.Key.Name, x.Sum(y => y.Debit), x.Sum(y => y.Credit)))
            .OrderBy(x => x.Code)
            .ToList();
        var summaries = companyReports.Select(x =>
        {
            decimal Total(AccountType type) => x.Report.Balances.Where(row => row.Type == type).Sum(row => row.DisplayAmount);
            var revenue = Total(AccountType.Revenue);
            var expenses = Total(AccountType.Expense);
            return new GroupCompanyFinancialSummary(
                x.Company.Id, x.Company.LegalName, x.Company.BaseCurrency,
                revenue, expenses, revenue - expenses,
                Total(AccountType.Asset), Total(AccountType.Liability), Total(AccountType.Equity));
        }).ToList();

        return new(group.OrganisationGroupId, group.Name, currencies[0], summaries, new(balances, trial));
    }
}
