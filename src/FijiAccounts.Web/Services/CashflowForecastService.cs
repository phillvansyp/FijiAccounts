using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record CashflowPeriodSummary(decimal ExpectedReceipts, decimal ExpectedPayments)
{
    public decimal NetMovement => ExpectedReceipts - ExpectedPayments;
}

public sealed record CashflowSourceBreakdown(
    decimal PostedReceipts,
    decimal PostedPayments,
    decimal RecurringReceipts,
    decimal RecurringPayments,
    decimal PlannedPurchasePayments);

public sealed record CashflowMonthlyProjection(
    DateOnly MonthStart,
    DateOnly MonthEnd,
    CashflowPeriodSummary Summary,
    CashflowSourceBreakdown Sources);

public sealed record CashflowForecast(
    CashflowPeriodSummary Today,
    CashflowPeriodSummary Next7Days,
    CashflowPeriodSummary Next30Days,
    CashflowPeriodSummary Next60Days,
    CashflowPeriodSummary Next90Days,
    CashflowPeriodSummary Next12Months,
    IReadOnlyList<CashflowMonthlyProjection> Monthly);

public sealed class CashflowForecastService(ApplicationDbContext db)
{
    public Task<CashflowForecast> GetAsync(Guid organisationId, CancellationToken ct = default) =>
        GetAsync(organisationId, DateOnly.FromDateTime(DateTime.Today), ct);

    public async Task<CashflowForecast> GetAsync(
        Guid organisationId,
        DateOnly asAt,
        CancellationToken ct = default)
    {
        var firstMonth = new DateOnly(asAt.Year, asAt.Month, 1);
        var horizon = firstMonth.AddMonths(12).AddDays(-1);
        var organisation = await db.Organisations.AsNoTracking()
            .SingleAsync(x => x.Id == organisationId, ct);
        var events = new List<CashflowEvent>();

        events.AddRange(await db.SalesInvoices.AsNoTracking()
            .Where(x => x.OrganisationId == organisationId && x.DueDate >= asAt &&
                x.DueDate <= horizon && x.AmountPaid + x.AmountCredited < x.Total &&
                x.Status != InvoiceStatus.Draft && x.Status != InvoiceStatus.Voided)
            .Select(x => new CashflowEvent(x.DueDate,
                x.Total - x.AmountPaid - x.AmountCredited,
                CashflowDirection.Receipt, CashflowSource.PostedDocument))
            .ToListAsync(ct));

        events.AddRange(await db.SupplierBills.AsNoTracking()
            .Where(x => x.OrganisationId == organisationId && x.DueDate >= asAt &&
                x.DueDate <= horizon && x.AmountPaid + x.AmountCredited < x.Total &&
                x.Status != BillStatus.Voided)
            .Select(x => new CashflowEvent(x.DueDate,
                x.Total - x.AmountPaid - x.AmountCredited,
                CashflowDirection.Payment, CashflowSource.PostedDocument))
            .ToListAsync(ct));

        var recurringSales = await db.RecurringSalesInvoices.AsNoTracking()
            .Include(x => x.Lines)
            .Where(x => x.OrganisationId == organisationId && x.IsActive &&
                x.Status == RecurringSalesInvoiceStatus.Active && x.NextInvoiceDate <= horizon)
            .ToListAsync(ct);
        foreach (var template in recurringSales)
        {
            for (var date = template.NextInvoiceDate; date <= horizon; date =
                 RecurringSalesInvoiceService.GetNextDate(date, template.Frequency, template.StartDate))
            {
                var dueDate = date.AddDays(template.DueDays);
                if (dueDate <= horizon)
                {
                    events.Add(new(dueDate < asAt ? asAt : dueDate,
                        GrossTotal(template.Lines, date, organisation.BaseCurrency),
                        CashflowDirection.Receipt, CashflowSource.RecurringTemplate));
                }
            }
        }

        var recurringBills = await db.RecurringSupplierBills.AsNoTracking()
            .Include(x => x.Lines)
            .Where(x => x.OrganisationId == organisationId && x.IsActive &&
                x.Status == RecurringSupplierBillStatus.Active && x.NextBillDate <= horizon)
            .ToListAsync(ct);
        foreach (var template in recurringBills)
        {
            for (var date = template.NextBillDate; date <= horizon; date =
                 RecurringSupplierBillService.GetNextDate(date, template.Frequency, template.StartDate))
            {
                var dueDate = date.AddDays(template.DueDays);
                if (dueDate <= horizon)
                {
                    events.Add(new(dueDate < asAt ? asAt : dueDate,
                        GrossTotal(template.Lines, date, organisation.BaseCurrency),
                        CashflowDirection.Payment, CashflowSource.RecurringTemplate));
                }
            }
        }

        events.AddRange(await db.PurchaseOrders.AsNoTracking()
            .Where(x => x.OrganisationId == organisationId && x.SupplierBillId == null &&
                x.Status != PurchaseOrderStatus.Draft && x.Status != PurchaseOrderStatus.Cancelled &&
                x.Status != PurchaseOrderStatus.Closed && (x.ExpectedDate ?? x.OrderDate) <= horizon)
            .Select(x => new CashflowEvent(
                (x.ExpectedDate ?? x.OrderDate) < asAt ? asAt : x.ExpectedDate ?? x.OrderDate,
                x.Total, CashflowDirection.Payment, CashflowSource.PlannedPurchase))
            .ToListAsync(ct));

        var months = Enumerable.Range(0, 12).Select(offset =>
        {
            var monthStart = firstMonth.AddMonths(offset);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var effectiveStart = monthStart < asAt ? asAt : monthStart;
            var monthEvents = events.Where(x => x.Date >= effectiveStart && x.Date <= monthEnd).ToList();
            return new CashflowMonthlyProjection(
                monthStart, monthEnd, Summary(monthEvents), Breakdown(monthEvents));
        }).ToList();

        return new(
            Cumulative(events, asAt),
            Cumulative(events, asAt.AddDays(7)),
            Cumulative(events, asAt.AddDays(30)),
            Cumulative(events, asAt.AddDays(60)),
            Cumulative(events, asAt.AddDays(90)),
            Cumulative(events, horizon),
            months);
    }

    private static decimal GrossTotal<TLine>(IEnumerable<TLine> lines, DateOnly date, string currency)
    {
        var schedule = new FijiVatSchedule();
        return lines.Sum(line =>
        {
            var values = line switch
            {
                RecurringSalesInvoiceLine sales => (sales.Quantity, sales.UnitPrice, sales.VatTreatment),
                RecurringSupplierBillLine purchase => (purchase.Quantity, purchase.UnitPrice, purchase.VatTreatment),
                _ => throw new ArgumentOutOfRangeException(nameof(lines))
            };
            return schedule.CalculateFromExclusive(
                new Money(values.Quantity * values.UnitPrice, currency).Round(),
                date, values.VatTreatment).Inclusive.Amount;
        });
    }

    private static CashflowPeriodSummary Cumulative(IEnumerable<CashflowEvent> events, DateOnly limit) =>
        Summary(events.Where(x => x.Date <= limit));

    private static CashflowPeriodSummary Summary(IEnumerable<CashflowEvent> events)
    {
        var list = events.ToList();
        return new(
            list.Where(x => x.Direction == CashflowDirection.Receipt).Sum(x => x.Amount),
            list.Where(x => x.Direction == CashflowDirection.Payment).Sum(x => x.Amount));
    }

    private static CashflowSourceBreakdown Breakdown(IEnumerable<CashflowEvent> events)
    {
        var list = events.ToList();
        decimal Total(CashflowDirection direction, CashflowSource source) =>
            list.Where(x => x.Direction == direction && x.Source == source).Sum(x => x.Amount);
        return new(
            Total(CashflowDirection.Receipt, CashflowSource.PostedDocument),
            Total(CashflowDirection.Payment, CashflowSource.PostedDocument),
            Total(CashflowDirection.Receipt, CashflowSource.RecurringTemplate),
            Total(CashflowDirection.Payment, CashflowSource.RecurringTemplate),
            Total(CashflowDirection.Payment, CashflowSource.PlannedPurchase));
    }

    private enum CashflowDirection { Receipt, Payment }
    private enum CashflowSource { PostedDocument, RecurringTemplate, PlannedPurchase }
    private sealed record CashflowEvent(
        DateOnly Date, decimal Amount, CashflowDirection Direction, CashflowSource Source);
}
