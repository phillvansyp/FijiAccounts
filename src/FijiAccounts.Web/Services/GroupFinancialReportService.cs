using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record GroupCompanyFinancialSummary(
    Guid OrganisationId,
    string LegalName,
    string SourceCurrency,
    decimal PeriodAverageRate,
    decimal ClosingRate,
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
                x.PresentationCurrency,
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

        var storedRates = await db.GroupExchangeRates
            .AsNoTracking()
            .Where(x =>
                x.OrganisationGroupId == group.OrganisationGroupId &&
                x.ToCurrency == group.PresentationCurrency &&
                x.EffectiveDate <= to)
            .OrderByDescending(x => x.EffectiveDate)
            .ToListAsync(cancellationToken);

        var companyReports = new List<TranslatedCompanyReport>();
        foreach (var company in group.Companies)
        {
            var averageRate = ResolveRate(
                company.BaseCurrency,
                group.PresentationCurrency,
                GroupExchangeRateType.PeriodAverage,
                storedRates,
                to);
            var closingRate = ResolveRate(
                company.BaseCurrency,
                group.PresentationCurrency,
                GroupExchangeRateType.Closing,
                storedRates,
                to);
            var sourceReport = await financialReports.GetAsync(company.Id, from, to, cancellationToken);
            companyReports.Add(new(
                company,
                Translate(sourceReport, averageRate, closingRate),
                averageRate,
                closingRate));
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
                x.PeriodAverageRate, x.ClosingRate,
                revenue, expenses, revenue - expenses,
                Total(AccountType.Asset), Total(AccountType.Liability), Total(AccountType.Equity));
        }).ToList();

        return new(
            group.OrganisationGroupId,
            group.Name,
            group.PresentationCurrency,
            summaries,
            new(balances, trial));
    }

    private static decimal ResolveRate(
        string sourceCurrency,
        string presentationCurrency,
        GroupExchangeRateType type,
        IReadOnlyList<GroupExchangeRate> rates,
        DateOnly reportDate)
    {
        if (sourceCurrency.Equals(presentationCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return 1m;
        }

        return rates.FirstOrDefault(x =>
                   x.FromCurrency.Equals(sourceCurrency, StringComparison.OrdinalIgnoreCase) &&
                   x.Type == type)?.Rate
               ?? throw new InvalidOperationException(
                   $"Add a {RateLabel(type)} {sourceCurrency} to {presentationCurrency} exchange rate on or before {reportDate:dd MMM yyyy}.");
    }

    private static FinancialReportData Translate(
        FinancialReportData source,
        decimal averageRate,
        decimal closingRate)
    {
        decimal Convert(decimal amount, decimal rate) =>
            Math.Round(amount * rate, 2, MidpointRounding.AwayFromZero);

        var balances = source.Balances.Select(x =>
        {
            var rate = x.Type is AccountType.Revenue or AccountType.Expense
                ? averageRate
                : closingRate;
            return x with { DisplayAmount = Convert(x.DisplayAmount, rate) };
        }).ToList();
        var trial = source.TrialBalance
            .Select(x => x with
            {
                Debit = Convert(x.Debit, closingRate),
                Credit = Convert(x.Credit, closingRate)
            })
            .ToList();

        return new(balances, trial);
    }

    private static string RateLabel(GroupExchangeRateType type) =>
        type == GroupExchangeRateType.PeriodAverage ? "period-average" : "closing";

    private sealed record TranslatedCompanyReport(
        Organisation Company,
        FinancialReportData Report,
        decimal PeriodAverageRate,
        decimal ClosingRate);
}
