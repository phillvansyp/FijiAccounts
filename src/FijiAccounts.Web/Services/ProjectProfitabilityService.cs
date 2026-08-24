using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record ProjectCostCodePerformance(
    Guid CostCodeId,
    string Code,
    string Name,
    decimal Budget,
    decimal ActualCost,
    decimal RemainingBudget);

public sealed record ProjectProfitability(
    Guid ProjectId,
    decimal ActualRevenue,
    decimal ActualCost,
    decimal ActualMargin,
    decimal ForecastCostToComplete,
    decimal? CostProgressPercent,
    decimal UncodedActualCost,
    IReadOnlyList<ProjectCostCodePerformance> CostCodes);

public sealed class ProjectProfitabilityService(
    ApplicationDbContext db,
    ProjectService projects)
{
    public async Task<IReadOnlyList<ProjectProfitability>> GetAsync(
        string userId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var accessibleProjects = await projects.ListAsync(
            userId, organisationId, cancellationToken);
        var projectIds = accessibleProjects.Select(x => x.Id).ToArray();
        var actuals = await db.PostedJournalLines.AsNoTracking()
            .Where(x => x.ProjectId != null && projectIds.Contains(x.ProjectId.Value))
            .GroupBy(x => new { ProjectId = x.ProjectId!.Value, x.ProjectCostCodeId, x.LedgerAccount.Type })
            .Select(x => new ActualAmount(
                x.Key.ProjectId,
                x.Key.ProjectCostCodeId,
                x.Key.Type,
                x.Key.Type == AccountType.Revenue
                    ? x.Sum(line => line.Credit - line.Debit)
                    : x.Key.Type == AccountType.Expense
                        ? x.Sum(line => line.Debit - line.Credit)
                        : 0m))
            .ToListAsync(cancellationToken);

        return accessibleProjects.Select(project =>
        {
            var projectActuals = actuals.Where(x => x.ProjectId == project.Id).ToList();
            var revenue = projectActuals.Where(x => x.Type == AccountType.Revenue).Sum(x => x.Amount);
            var cost = projectActuals.Where(x => x.Type == AccountType.Expense).Sum(x => x.Amount);
            var costCodes = project.CostCodes.Where(x => x.IsActive).Select(code =>
            {
                var actual = projectActuals
                    .Where(x => x.ProjectCostCodeId == code.Id && x.Type == AccountType.Expense)
                    .Sum(x => x.Amount);
                return new ProjectCostCodePerformance(
                    code.Id, code.Code, code.Name, code.BudgetAmount,
                    actual, code.BudgetAmount - actual);
            }).ToList();
            var codedCost = costCodes.Sum(x => x.ActualCost);
            return new ProjectProfitability(
                project.Id,
                revenue,
                cost,
                revenue - cost,
                Math.Max(0m, project.ForecastCost - cost),
                project.ForecastCost == 0 ? null : cost / project.ForecastCost * 100m,
                cost - codedCost,
                costCodes);
        }).ToList();
    }

    private sealed record ActualAmount(
        Guid ProjectId,
        Guid? ProjectCostCodeId,
        AccountType Type,
        decimal Amount);
}
