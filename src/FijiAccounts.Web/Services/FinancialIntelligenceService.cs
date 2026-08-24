using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public enum FinancialInsightSeverity
{
    Watch,
    High
}

public enum FinancialInsightCategory
{
    GrossMargin,
    SupplierCost,
    CustomerConcentration,
    SupplierConcentration
}

public sealed record FinancialInsight(
    FinancialInsightCategory Category,
    FinancialInsightSeverity Severity,
    string Title,
    string Explanation);

public sealed record FinancialIntelligenceSummary(
    DateOnly AsAt,
    decimal CurrentRevenue,
    decimal CurrentGrossProfit,
    decimal? CurrentGrossMarginPercent,
    decimal? PriorGrossMarginPercent,
    IReadOnlyList<FinancialInsight> Insights);

public sealed class FinancialIntelligenceService(
    ApplicationDbContext db,
    TenantAccessService access)
{
    private const decimal MarginWatchDecline = 5m;
    private const decimal MarginHighDecline = 10m;
    private const decimal CostWatchIncrease = 10m;
    private const decimal CostHighIncrease = 25m;
    private const decimal MinimumCostImpact = 50m;
    private const decimal ConcentrationWatch = 40m;
    private const decimal ConcentrationHigh = 60m;

    public async Task<FinancialIntelligenceSummary> GetAsync(
        string userId,
        Guid organisationId,
        DateOnly? asAt = null,
        CancellationToken ct = default)
    {
        if (await access.FindAsync(userId, organisationId) is null)
        {
            throw new UnauthorizedAccessException(
                "You cannot view financial intelligence for this organisation.");
        }

        var end = asAt ?? DateOnly.FromDateTime(DateTime.Today);
        var currentStart = end.AddDays(-29);
        var priorEnd = currentStart.AddDays(-1);
        var priorStart = priorEnd.AddDays(-29);
        var insights = new List<FinancialInsight>();

        var profitLines = await db.PostedJournalLines.AsNoTracking()
            .Where(x =>
                x.PostedJournal.OrganisationId == organisationId &&
                x.PostedJournal.EntryDate >= priorStart &&
                x.PostedJournal.EntryDate <= end &&
                (x.LedgerAccount.Type == AccountType.Revenue ||
                 x.LedgerAccount.Type == AccountType.Expense &&
                 x.LedgerAccount.Code.StartsWith("5")))
            .Select(x => new
            {
                x.PostedJournal.EntryDate,
                x.LedgerAccount.Type,
                x.Debit,
                x.Credit
            })
            .ToListAsync(ct);

        var currentRevenue = profitLines
            .Where(x => x.EntryDate >= currentStart && x.Type == AccountType.Revenue)
            .Sum(x => x.Credit - x.Debit);
        var currentCost = profitLines
            .Where(x => x.EntryDate >= currentStart && x.Type == AccountType.Expense)
            .Sum(x => x.Debit - x.Credit);
        var priorRevenue = profitLines
            .Where(x => x.EntryDate <= priorEnd && x.Type == AccountType.Revenue)
            .Sum(x => x.Credit - x.Debit);
        var priorCost = profitLines
            .Where(x => x.EntryDate <= priorEnd && x.Type == AccountType.Expense)
            .Sum(x => x.Debit - x.Credit);
        var currentMargin = Margin(currentRevenue, currentCost);
        var priorMargin = Margin(priorRevenue, priorCost);

        if (currentMargin is decimal current && priorMargin is decimal prior)
        {
            var decline = prior - current;
            if (decline >= MarginWatchDecline)
            {
                insights.Add(new(
                    FinancialInsightCategory.GrossMargin,
                    decline >= MarginHighDecline
                        ? FinancialInsightSeverity.High
                        : FinancialInsightSeverity.Watch,
                    "Gross margin has deteriorated",
                    $"Gross margin is {current:N1}% for the latest 30 days, down {decline:N1} percentage points from {prior:N1}% in the preceding 30 days."));
            }
        }

        var supplierCosts = await db.SupplierBillLines.AsNoTracking()
            .Where(x =>
                x.SupplierBill.OrganisationId == organisationId &&
                x.SupplierBill.Status != BillStatus.Voided &&
                x.SupplierBill.BillDate >= priorStart &&
                x.SupplierBill.BillDate <= end)
            .Select(x => new SupplierCostLine(
                x.SupplierBill.SupplierId,
                x.SupplierBill.Supplier.Name,
                x.SupplierBill.BillDate,
                x.ProductItemId,
                x.Description,
                x.Quantity,
                x.NetAmount))
            .ToListAsync(ct);

        var costMovements = supplierCosts
            .Where(x => x.Quantity > 0)
            .GroupBy(x => new
            {
                x.SupplierId,
                Key = x.ProductItemId?.ToString() ?? x.Description.Trim().ToUpperInvariant()
            })
            .Select(group =>
            {
                var currentLines = group.Where(x => x.BillDate >= currentStart).ToList();
                var priorLines = group.Where(x => x.BillDate <= priorEnd).ToList();
                var currentQuantity = currentLines.Sum(x => x.Quantity);
                var priorQuantity = priorLines.Sum(x => x.Quantity);
                var currentPrice = currentQuantity == 0 ? 0 : currentLines.Sum(x => x.NetAmount) / currentQuantity;
                var priorPrice = priorQuantity == 0 ? 0 : priorLines.Sum(x => x.NetAmount) / priorQuantity;
                var increase = priorPrice <= 0 ? 0 : (currentPrice - priorPrice) / priorPrice * 100m;
                var impact = Math.Max(0, currentPrice - priorPrice) * currentQuantity;
                var sample = currentLines.FirstOrDefault() ?? priorLines.First();
                return new
                {
                    sample.SupplierName,
                    sample.Description,
                    Increase = increase,
                    Impact = impact,
                    HasBothPeriods = currentQuantity > 0 && priorQuantity > 0
                };
            })
            .Where(x =>
                x.HasBothPeriods &&
                x.Increase >= CostWatchIncrease &&
                x.Impact >= MinimumCostImpact)
            .OrderByDescending(x => x.Impact)
            .Take(5)
            .ToList();

        insights.AddRange(costMovements.Select(x => new FinancialInsight(
            FinancialInsightCategory.SupplierCost,
            x.Increase >= CostHighIncrease
                ? FinancialInsightSeverity.High
                : FinancialInsightSeverity.Watch,
            $"Supplier cost increased: {x.Description}",
            $"{x.SupplierName}'s average unit cost rose {x.Increase:N1}% versus the preceding 30 days, adding approximately ${x.Impact:N2} at current volumes.")));

        var customerBalances = await db.SalesInvoices.AsNoTracking()
            .Where(x =>
                x.OrganisationId == organisationId &&
                x.Status != InvoiceStatus.Draft &&
                x.Status != InvoiceStatus.Voided &&
                x.AmountPaid + x.AmountCredited < x.Total)
            .GroupBy(x => new { x.CustomerId, x.Customer.Name })
            .Select(x => new PartyBalance(
                x.Key.CustomerId,
                x.Key.Name,
                x.Sum(y => y.Total - y.AmountPaid - y.AmountCredited)))
            .ToListAsync(ct);
        AddConcentrationInsight(
            insights,
            customerBalances,
            FinancialInsightCategory.CustomerConcentration,
            "receivables",
            "customer");

        var supplierBalances = await db.SupplierBills.AsNoTracking()
            .Where(x =>
                x.OrganisationId == organisationId &&
                x.Status != BillStatus.Voided &&
                x.AmountPaid + x.AmountCredited < x.Total)
            .GroupBy(x => new { x.SupplierId, x.Supplier.Name })
            .Select(x => new PartyBalance(
                x.Key.SupplierId,
                x.Key.Name,
                x.Sum(y => y.Total - y.AmountPaid - y.AmountCredited)))
            .ToListAsync(ct);
        AddConcentrationInsight(
            insights,
            supplierBalances,
            FinancialInsightCategory.SupplierConcentration,
            "payables",
            "supplier");

        return new(
            end,
            currentRevenue,
            currentRevenue - currentCost,
            currentMargin,
            priorMargin,
            insights
                .OrderByDescending(x => x.Severity)
                .ThenBy(x => x.Category)
                .ToList());
    }

    private static decimal? Margin(decimal revenue, decimal cost) =>
        revenue <= 0
            ? null
            : decimal.Round((revenue - cost) / revenue * 100m, 2,
                MidpointRounding.AwayFromZero);

    private static void AddConcentrationInsight(
        ICollection<FinancialInsight> insights,
        IReadOnlyCollection<PartyBalance> balances,
        FinancialInsightCategory category,
        string balanceLabel,
        string partyLabel)
    {
        var total = balances.Sum(x => x.Amount);
        var largest = balances.OrderByDescending(x => x.Amount).FirstOrDefault();
        if (largest is null || total <= 0)
        {
            return;
        }

        var percentage = largest.Amount / total * 100m;
        if (percentage < ConcentrationWatch)
        {
            return;
        }

        insights.Add(new(
            category,
            percentage >= ConcentrationHigh
                ? FinancialInsightSeverity.High
                : FinancialInsightSeverity.Watch,
            $"High {partyLabel} concentration",
            $"{largest.Name} represents {percentage:N1}% (${largest.Amount:N2}) of outstanding {balanceLabel}."));
    }

    private sealed record SupplierCostLine(
        Guid SupplierId,
        string SupplierName,
        DateOnly BillDate,
        Guid? ProductItemId,
        string Description,
        decimal Quantity,
        decimal NetAmount);

    private sealed record PartyBalance(Guid PartyId, string Name, decimal Amount);
}
