using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record BudgetReportRequest(
    Guid OrganisationId,
    int Year,
    decimal AlertThresholdPercent = 10m,
    decimal AlertThresholdAmount = 100m);

public sealed record BudgetVarianceRow(
    Guid AccountId,
    string Code,
    string Name,
    AccountType Type,
    decimal Budget,
    decimal Actual,
    decimal FavourableVariance,
    decimal AdverseVariance,
    decimal? AdverseVariancePercent,
    bool RequiresAttention);

public sealed record MonthlyBudgetVariance(
    DateOnly Month,
    decimal RevenueBudget,
    decimal RevenueActual,
    decimal ExpenseBudget,
    decimal ExpenseActual,
    decimal FavourableVariance,
    decimal AdverseVariance,
    bool RequiresAttention);

public sealed record BudgetPerformanceReport(
    int Year,
    decimal AlertThresholdPercent,
    decimal AlertThresholdAmount,
    IReadOnlyList<LedgerAccount> Accounts,
    IReadOnlyList<BudgetVarianceRow> AccountsPerformance,
    IReadOnlyList<MonthlyBudgetVariance> Months)
{
    public int AlertCount =>
        AccountsPerformance.Count(x => x.RequiresAttention);
    public decimal RevenueBudget =>
        AccountsPerformance.Where(x => x.Type == AccountType.Revenue).Sum(x => x.Budget);
    public decimal RevenueActual =>
        AccountsPerformance.Where(x => x.Type == AccountType.Revenue).Sum(x => x.Actual);
    public decimal ExpenseBudget =>
        AccountsPerformance.Where(x => x.Type == AccountType.Expense).Sum(x => x.Budget);
    public decimal ExpenseActual =>
        AccountsPerformance.Where(x => x.Type == AccountType.Expense).Sum(x => x.Actual);
    public decimal BudgetProfit => RevenueBudget - ExpenseBudget;
    public decimal ActualProfit => RevenueActual - ExpenseActual;
}

public sealed class BudgetReportingService(
    ApplicationDbContext db,
    TenantAccessService access)
{
    public async Task<BudgetPerformanceReport> GetAsync(
        string userId,
        BudgetReportRequest request,
        CancellationToken ct = default)
    {
        if (await access.FindAsync(userId, request.OrganisationId) is null)
        {
            throw new UnauthorizedAccessException(
                "You cannot view budgets for this organisation.");
        }

        if (request.Year is < 2000 or > 2200 ||
            request.AlertThresholdPercent is < 0 or > 10_000 ||
            request.AlertThresholdAmount < 0)
        {
            throw new InvalidOperationException(
                "Enter a valid year and non-negative variance thresholds.");
        }

        var from = new DateOnly(request.Year, 1, 1);
        var to = new DateOnly(request.Year, 12, 31);
        var accounts = await db.LedgerAccounts.AsNoTracking()
            .Where(x =>
                x.OrganisationId == request.OrganisationId &&
                (x.Type == AccountType.Revenue || x.Type == AccountType.Expense))
            .OrderBy(x => x.Code)
            .ToListAsync(ct);
        var budgets = await db.AccountBudgets.AsNoTracking()
            .Where(x =>
                x.OrganisationId == request.OrganisationId &&
                x.Month >= from && x.Month <= to)
            .ToListAsync(ct);
        var actuals = await db.PostedJournalLines.AsNoTracking()
            .Where(x =>
                x.PostedJournal.OrganisationId == request.OrganisationId &&
                x.PostedJournal.EntryDate >= from &&
                x.PostedJournal.EntryDate <= to &&
                (x.LedgerAccount.Type == AccountType.Revenue ||
                 x.LedgerAccount.Type == AccountType.Expense))
            .GroupBy(x => new
            {
                x.LedgerAccountId,
                x.PostedJournal.EntryDate.Month,
                x.LedgerAccount.Type
            })
            .Select(x => new ActualAmount(
                x.Key.LedgerAccountId,
                x.Key.Month,
                x.Key.Type,
                x.Key.Type == AccountType.Revenue
                    ? x.Sum(y => y.Credit - y.Debit)
                    : x.Sum(y => y.Debit - y.Credit)))
            .ToListAsync(ct);

        var rows = accounts.Select(account =>
        {
            var budget = budgets.Where(x => x.LedgerAccountId == account.Id).Sum(x => x.Amount);
            var actual = actuals.Where(x => x.AccountId == account.Id).Sum(x => x.Amount);
            return CreateRow(account, budget, actual, request);
        }).Where(x => x.Budget != 0 || x.Actual != 0).ToList();

        var months = Enumerable.Range(1, 12).Select(month =>
        {
            var monthBudgets = budgets.Where(x => x.Month.Month == month).ToList();
            var revenueBudget = SumBudget(monthBudgets, accounts, AccountType.Revenue);
            var expenseBudget = SumBudget(monthBudgets, accounts, AccountType.Expense);
            var revenueActual = actuals
                .Where(x => x.Month == month && x.Type == AccountType.Revenue).Sum(x => x.Amount);
            var expenseActual = actuals
                .Where(x => x.Month == month && x.Type == AccountType.Expense).Sum(x => x.Amount);
            var favourable = (revenueActual - revenueBudget) + (expenseBudget - expenseActual);
            var adverse = Math.Max(0, -favourable);
            var baseline = revenueBudget + expenseBudget;
            return new MonthlyBudgetVariance(
                new DateOnly(request.Year, month, 1),
                revenueBudget,
                revenueActual,
                expenseBudget,
                expenseActual,
                favourable,
                adverse,
                IsAlert(adverse, baseline, request));
        }).ToList();

        return new(
            request.Year,
            request.AlertThresholdPercent,
            request.AlertThresholdAmount,
            accounts,
            rows,
            months);
    }

    private static BudgetVarianceRow CreateRow(
        LedgerAccount account,
        decimal budget,
        decimal actual,
        BudgetReportRequest request)
    {
        var favourable = account.Type == AccountType.Revenue
            ? actual - budget
            : budget - actual;
        var adverse = Math.Max(0, -favourable);
        var adversePercent = budget == 0
            ? (decimal?)null
            : adverse / budget * 100m;
        return new(
            account.Id,
            account.Code,
            account.Name,
            account.Type,
            budget,
            actual,
            favourable,
            adverse,
            adversePercent,
            IsAlert(adverse, budget, request));
    }

    private static bool IsAlert(
        decimal adverse,
        decimal baseline,
        BudgetReportRequest request) =>
        adverse > 0 &&
        adverse >= request.AlertThresholdAmount &&
        (baseline == 0 || adverse / baseline * 100m >= request.AlertThresholdPercent);

    private static decimal SumBudget(
        IEnumerable<AccountBudget> budgets,
        IReadOnlyCollection<LedgerAccount> accounts,
        AccountType type)
    {
        var accountIds = accounts.Where(x => x.Type == type).Select(x => x.Id).ToHashSet();
        return budgets.Where(x => accountIds.Contains(x.LedgerAccountId)).Sum(x => x.Amount);
    }

    private sealed record ActualAmount(
        Guid AccountId,
        int Month,
        AccountType Type,
        decimal Amount);
}
