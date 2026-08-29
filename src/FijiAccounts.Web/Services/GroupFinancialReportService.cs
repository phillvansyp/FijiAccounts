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
    FinancialReportData Eliminations,
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
        var accountMappings = await db.GroupLedgerAccountMappings
            .AsNoTracking()
            .Where(x => x.OrganisationGroupId == group.OrganisationGroupId &&
                        x.GroupLedgerAccount.IsActive)
            .Select(x => new CompanyAccountMapping(
                x.OrganisationId,
                x.LedgerAccount.Code,
                x.GroupLedgerAccount.Code,
                x.GroupLedgerAccount.Name,
                x.GroupLedgerAccount.Type))
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
            var sourceReport = ApplyGroupAccountMappings(
                await financialReports.GetAsync(company.Id, from, to, cancellationToken),
                company.Id,
                accountMappings);
            companyReports.Add(new(
                company,
                Translate(sourceReport, averageRate, closingRate),
                averageRate,
                closingRate));
        }

        var eliminations = await GetEliminationsAsync(
            group.OrganisationGroupId,
            from,
            to,
            cancellationToken);
        var balances = companyReports.SelectMany(x => x.Report.Balances)
            .Concat(eliminations.Balances)
            .GroupBy(x => new { x.Code, x.Name, x.Type })
            .Select(x => new FinancialAccountBalance(x.Key.Code, x.Key.Name, x.Key.Type, x.Sum(y => y.DisplayAmount)))
            .OrderBy(x => x.Code)
            .ToList();
        var trial = companyReports.SelectMany(x => x.Report.TrialBalance)
            .Concat(eliminations.TrialBalance)
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
            eliminations,
            new(balances, trial));
    }

    private async Task<FinancialReportData> GetEliminationsAsync(
        Guid groupId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var lines = await db.GroupEliminationJournalLines
            .AsNoTracking()
            .Where(x =>
                x.GroupEliminationJournal.OrganisationGroupId == groupId &&
                x.GroupEliminationJournal.EntryDate <= to)
            .Select(x => new
            {
                x.AccountCode,
                x.AccountName,
                x.AccountType,
                x.Debit,
                x.Credit,
                x.GroupEliminationJournal.EntryDate
            })
            .ToListAsync(cancellationToken);

        var all = lines
            .GroupBy(x => new { x.AccountCode, x.AccountName, x.AccountType })
            .Select(x => new
            {
                Code = x.Key.AccountCode,
                Name = x.Key.AccountName,
                Type = x.Key.AccountType,
                Debit = x.Sum(y => y.Debit),
                Credit = x.Sum(y => y.Credit)
            })
            .ToList();
        var trial = all
            .Select(x =>
            {
                var net = x.Debit - x.Credit;
                return new TrialBalanceRow(
                    x.Code,
                    x.Name,
                    Math.Max(net, 0m),
                    Math.Max(-net, 0m));
            })
            .Where(x => x.Debit != 0m || x.Credit != 0m)
            .ToList();
        var accumulatedProfit = all
            .Where(x => x.Type is AccountType.Revenue or AccountType.Expense)
            .Sum(x => x.Credit - x.Debit);
        var currentProfit = lines
            .Where(x =>
                x.EntryDate >= from &&
                x.AccountType is AccountType.Revenue or AccountType.Expense)
            .GroupBy(x => new { x.AccountCode, x.AccountName, x.AccountType })
            .Select(x => new FinancialAccountBalance(
                x.Key.AccountCode,
                x.Key.AccountName,
                x.Key.AccountType,
                x.Key.AccountType == AccountType.Revenue
                    ? x.Sum(y => y.Credit - y.Debit)
                    : x.Sum(y => y.Debit - y.Credit)))
            .Where(x => x.DisplayAmount != 0m);
        var balances = all
            .Where(x => x.Type is AccountType.Asset or AccountType.Liability or AccountType.Equity)
            .Select(x => new FinancialAccountBalance(
                x.Code,
                x.Name,
                x.Type,
                x.Type == AccountType.Asset ? x.Debit - x.Credit : x.Credit - x.Debit))
            .Concat(
                accumulatedProfit == 0m
                    ? []
                    : [new FinancialAccountBalance("", "Accumulated earnings", AccountType.Equity, accumulatedProfit)])
            .Concat(currentProfit)
            .Where(x => x.DisplayAmount != 0m)
            .OrderBy(x => x.Code)
            .ToList();

        return new(balances, trial);
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

    private static FinancialReportData ApplyGroupAccountMappings(
        FinancialReportData source,
        Guid organisationId,
        IReadOnlyList<CompanyAccountMapping> mappings)
    {
        var byCode = mappings
            .Where(x => x.OrganisationId == organisationId)
            .ToDictionary(x => x.CompanyAccountCode, StringComparer.OrdinalIgnoreCase);
        var balances = source.Balances.Select(row =>
        {
            if (!byCode.TryGetValue(row.Code, out var mapping))
            {
                return row;
            }

            return new FinancialAccountBalance(
                mapping.GroupAccountCode,
                mapping.GroupAccountName,
                mapping.GroupAccountType,
                row.DisplayAmount);
        }).ToList();
        var trial = source.TrialBalance.Select(row =>
        {
            if (!byCode.TryGetValue(row.Code, out var mapping))
            {
                return row;
            }

            return new TrialBalanceRow(
                mapping.GroupAccountCode,
                mapping.GroupAccountName,
                row.Debit,
                row.Credit);
        }).ToList();
        return new(balances, trial);
    }

    private static string RateLabel(GroupExchangeRateType type) =>
        type == GroupExchangeRateType.PeriodAverage ? "period-average" : "closing";

    private sealed record TranslatedCompanyReport(
        Organisation Company,
        FinancialReportData Report,
        decimal PeriodAverageRate,
        decimal ClosingRate);

    private sealed record CompanyAccountMapping(
        Guid OrganisationId,
        string CompanyAccountCode,
        string GroupAccountCode,
        string GroupAccountName,
        AccountType GroupAccountType);
}
