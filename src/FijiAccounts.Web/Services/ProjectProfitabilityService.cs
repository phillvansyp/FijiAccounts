using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record ProjectCostCodePerformance(
    Guid CostCodeId,
    string Code,
    string Name,
    ProjectCostCategory Category,
    decimal Budget,
    decimal ActualCost,
    decimal CommittedCost,
    decimal RemainingBudgetAfterCommitment);

public sealed record ProjectCostCategoryPerformance(
    ProjectCostCategory Category,
    decimal Budget,
    decimal ActualCost,
    decimal CommittedCost,
    decimal RemainingBudgetAfterCommitment);

public sealed record ProjectProfitability(
    Guid ProjectId,
    decimal ActualRevenue,
    decimal ActualCost,
    decimal ActualMargin,
    decimal CommittedCost,
    decimal ForecastCostToComplete,
    decimal UncommittedForecastCost,
    decimal? CostProgressPercent,
    decimal UncodedActualCost,
    decimal UncodedCommittedCost,
    IReadOnlyList<ProjectCostCategoryPerformance> Categories,
    IReadOnlyList<ProjectCostCodePerformance> CostCodes);

public sealed class ProjectProfitabilityService(
    ApplicationDbContext db,
    ProjectService projects)
{
    private static readonly ProjectCostCategory[] CategoryOrder =
    [
        ProjectCostCategory.Labour,
        ProjectCostCategory.Materials,
        ProjectCostCategory.Equipment,
        ProjectCostCategory.Subcontractors,
        ProjectCostCategory.Other
    ];

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
        var commitments = await db.PurchaseOrderLines.AsNoTracking()
            .Where(x => x.ProjectId != null && projectIds.Contains(x.ProjectId.Value) &&
                x.PurchaseOrder.SupplierBillId == null &&
                (x.PurchaseOrder.Status == PurchaseOrderStatus.Approved ||
                 x.PurchaseOrder.Status == PurchaseOrderStatus.Sent ||
                 x.PurchaseOrder.Status == PurchaseOrderStatus.PartiallyReceived ||
                 x.PurchaseOrder.Status == PurchaseOrderStatus.Received))
            .GroupBy(x => new { ProjectId = x.ProjectId!.Value, x.ProjectCostCodeId })
            .Select(x => new CommittedAmount(
                x.Key.ProjectId, x.Key.ProjectCostCodeId, x.Sum(line => line.NetAmount)))
            .ToListAsync(cancellationToken);

        return accessibleProjects.Select(project =>
        {
            var projectActuals = actuals.Where(x => x.ProjectId == project.Id).ToList();
            var revenue = projectActuals.Where(x => x.Type == AccountType.Revenue).Sum(x => x.Amount);
            var cost = projectActuals.Where(x => x.Type == AccountType.Expense).Sum(x => x.Amount);
            var projectCommitments = commitments.Where(x => x.ProjectId == project.Id).ToList();
            var committed = projectCommitments.Sum(x => x.Amount);
            var categoryByCostCode = project.CostCodes.ToDictionary(
                x => x.Id, x => x.Category);
            var costCodes = project.CostCodes.Where(x => x.IsActive).Select(code =>
            {
                var actual = projectActuals
                    .Where(x => x.ProjectCostCodeId == code.Id && x.Type == AccountType.Expense)
                    .Sum(x => x.Amount);
                var codeCommitted = projectCommitments
                    .Where(x => x.ProjectCostCodeId == code.Id).Sum(x => x.Amount);
                return new ProjectCostCodePerformance(
                    code.Id, code.Code, code.Name, code.Category, code.BudgetAmount,
                    actual, codeCommitted, code.BudgetAmount - actual - codeCommitted);
            }).ToList();
            var categories = CategoryOrder.Select(category =>
            {
                var budget = project.CostCodes
                    .Where(x => x.IsActive && x.Category == category)
                    .Sum(x => x.BudgetAmount);
                var categoryActual = projectActuals
                    .Where(x => x.Type == AccountType.Expense &&
                        CategoryFor(x.ProjectCostCodeId, categoryByCostCode) == category)
                    .Sum(x => x.Amount);
                var categoryCommitted = projectCommitments
                    .Where(x => CategoryFor(x.ProjectCostCodeId, categoryByCostCode) == category)
                    .Sum(x => x.Amount);
                return new ProjectCostCategoryPerformance(
                    category,
                    budget,
                    categoryActual,
                    categoryCommitted,
                    budget - categoryActual - categoryCommitted);
            }).ToList();
            return new ProjectProfitability(
                project.Id,
                revenue,
                cost,
                revenue - cost,
                committed,
                Math.Max(0m, project.ForecastCost - cost),
                Math.Max(0m, project.ForecastCost - cost - committed),
                project.ForecastCost == 0 ? null : cost / project.ForecastCost * 100m,
                projectActuals.Where(x => x.Type == AccountType.Expense &&
                    x.ProjectCostCodeId is null).Sum(x => x.Amount),
                projectCommitments.Where(x => x.ProjectCostCodeId is null).Sum(x => x.Amount),
                categories,
                costCodes);
        }).ToList();
    }

    private static ProjectCostCategory CategoryFor(
        Guid? costCodeId,
        IReadOnlyDictionary<Guid, ProjectCostCategory> categoryByCostCode) =>
        costCodeId is Guid id && categoryByCostCode.TryGetValue(id, out var category)
            ? category
            : ProjectCostCategory.Other;

    private sealed record ActualAmount(
        Guid ProjectId,
        Guid? ProjectCostCodeId,
        AccountType Type,
        decimal Amount);

    private sealed record CommittedAmount(
        Guid ProjectId,
        Guid? ProjectCostCodeId,
        decimal Amount);
}
