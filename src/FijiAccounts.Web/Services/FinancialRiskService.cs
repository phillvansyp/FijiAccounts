using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record AgingRiskSummary(
    decimal Current,
    decimal Days1To30,
    decimal Days31To60,
    decimal Days61To90,
    decimal Over90)
{
    public decimal Overdue =>
        Days1To30 + Days31To60 + Days61To90 + Over90;

    public decimal Total =>
        Current + Overdue;

    public decimal OverduePercentage =>
        Total == 0
            ? 0
            : Overdue / Total * 100;

    public FinancialRiskLevel RiskLevel =>
        Over90 > 0
            ? FinancialRiskLevel.High
            : Overdue > 0
                ? FinancialRiskLevel.Watch
                : FinancialRiskLevel.Current;
}

public enum FinancialRiskLevel
{
    Current,
    Watch,
    High
}

public sealed record FinancialRiskSummary(
    AgingRiskSummary Receivables,
    AgingRiskSummary Payables);

public sealed class FinancialRiskService(
    ApplicationDbContext db)
{
    public async Task<FinancialRiskSummary> GetAsync(
        Guid organisationId,
        CancellationToken ct = default)
    {
        var today =
            DateOnly.FromDateTime(DateTime.Today);

        var receivables =
            await db.SalesInvoices
                .AsNoTracking()
                .Where(
                    x =>
                        x.OrganisationId == organisationId &&
                        x.Status != InvoiceStatus.Draft &&
                        x.Status != InvoiceStatus.Voided &&
                        x.AmountPaid + x.AmountCredited < x.Total)
                .Select(
                    x =>
                        new AgingBalance(
                            x.DueDate,
                            x.Total - x.AmountPaid - x.AmountCredited))
                .ToListAsync(ct);

        var payables =
            await db.SupplierBills
                .AsNoTracking()
                .Where(
                    x =>
                        x.OrganisationId == organisationId &&
                        x.Status != BillStatus.Voided &&
                        x.AmountPaid + x.AmountCredited < x.Total)
                .Select(
                    x =>
                        new AgingBalance(
                            x.DueDate,
                            x.Total - x.AmountPaid - x.AmountCredited))
                .ToListAsync(ct);

        return new(
            Summarise(receivables, today),
            Summarise(payables, today));
    }

    private static AgingRiskSummary Summarise(
        IEnumerable<AgingBalance> balances,
        DateOnly asAt)
    {
        decimal current = 0;
        decimal days1To30 = 0;
        decimal days31To60 = 0;
        decimal days61To90 = 0;
        decimal over90 = 0;

        foreach (var balance in balances)
        {
            var daysOverdue =
                asAt.DayNumber - balance.DueDate.DayNumber;

            if (daysOverdue <= 0)
            {
                current += balance.Amount;
            }
            else if (daysOverdue <= 30)
            {
                days1To30 += balance.Amount;
            }
            else if (daysOverdue <= 60)
            {
                days31To60 += balance.Amount;
            }
            else if (daysOverdue <= 90)
            {
                days61To90 += balance.Amount;
            }
            else
            {
                over90 += balance.Amount;
            }
        }

        return new(
            current,
            days1To30,
            days31To60,
            days61To90,
            over90);
    }

    private sealed record AgingBalance(
        DateOnly DueDate,
        decimal Amount);
}
