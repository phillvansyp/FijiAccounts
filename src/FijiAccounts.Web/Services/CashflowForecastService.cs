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
    decimal PlannedPurchasePayments,
    decimal ScenarioReceipts = 0m,
    decimal ScenarioPayments = 0m);

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
        BuildAsync(organisationId, DateOnly.FromDateTime(DateTime.Today), null, ct);

    public async Task<CashflowForecast> GetAsync(
        Guid organisationId,
        DateOnly asAt,
        CancellationToken ct = default) =>
        await BuildAsync(organisationId, asAt, null, ct);

    public async Task<CashflowForecast> GetForScenarioAsync(
        Guid organisationId,
        DateOnly asAt,
        Guid scenarioId,
        CancellationToken ct = default) =>
        await BuildAsync(organisationId, asAt, scenarioId, ct);

    private async Task<CashflowForecast> BuildAsync(
        Guid organisationId,
        DateOnly asAt,
        Guid? scenarioId,
        CancellationToken ct)
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
                CashflowDirection.Receipt, CashflowSource.PostedDocument, x.Id))
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
                        GrossTotal(template.Lines, date, organisation.BaseCurrency, organisation.CountryCode),
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
                        GrossTotal(template.Lines, date, organisation.BaseCurrency, organisation.CountryCode),
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

        if (scenarioId is Guid selectedScenarioId)
        {
            var scenario = await db.CashflowScenarios
                .AsNoTracking()
                .Include(x => x.Events)
                .SingleOrDefaultAsync(x =>
                    x.Id == selectedScenarioId &&
                    x.OrganisationId == organisationId &&
                    !x.IsArchived,
                    ct)
                ?? throw new InvalidOperationException("The selected cashflow scenario is not active in this organisation.");
            ApplyScenario(events, scenario.Events, asAt, horizon);
        }

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

    private static void ApplyScenario(
        List<CashflowEvent> events,
        IReadOnlyList<CashflowScenarioEvent> adjustments,
        DateOnly asAt,
        DateOnly horizon)
    {
        foreach (var adjustment in adjustments)
        {
            if (adjustment.Kind == CashflowScenarioEventKind.CustomerReceiptDelay)
            {
                var original = events.FirstOrDefault(x =>
                    x.Direction == CashflowDirection.Receipt &&
                    x.Source == CashflowSource.PostedDocument &&
                    x.SourceEntityId == adjustment.SalesInvoiceId);
                if (original is null) continue;
                events.Remove(original);
                if (adjustment.EventDate <= horizon)
                {
                    events.Add(original with
                    {
                        Date = adjustment.EventDate < asAt ? asAt : adjustment.EventDate,
                        Source = CashflowSource.Scenario
                    });
                }

                continue;
            }

            var direction = adjustment.Kind == CashflowScenarioEventKind.PlannedReceipt
                ? CashflowDirection.Receipt
                : CashflowDirection.Payment;
            foreach (var date in ScenarioDates(adjustment, asAt, horizon))
            {
                events.Add(new(
                    date,
                    adjustment.Amount,
                    direction,
                    CashflowSource.Scenario));
            }
        }
    }

    private static IEnumerable<DateOnly> ScenarioDates(
        CashflowScenarioEvent adjustment,
        DateOnly asAt,
        DateOnly horizon)
    {
        if (adjustment.Frequency == CashflowScenarioFrequency.OneOff)
        {
            if (adjustment.EventDate <= horizon)
            {
                yield return adjustment.EventDate < asAt ? asAt : adjustment.EventDate;
            }

            yield break;
        }

        var limit = adjustment.EndDate is DateOnly endDate && endDate < horizon
            ? endDate
            : horizon;
        for (var date = adjustment.EventDate; date <= limit; date = date.AddMonths(1))
        {
            if (date >= asAt) yield return date;
        }
    }

    private static decimal GrossTotal<TLine>(IEnumerable<TLine> lines, DateOnly date, string currency, string countryCode)
    {
        var schedule = IndirectTaxSchedules.For(countryCode);
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
            Total(CashflowDirection.Payment, CashflowSource.PlannedPurchase),
            Total(CashflowDirection.Receipt, CashflowSource.Scenario),
            Total(CashflowDirection.Payment, CashflowSource.Scenario));
    }

    private enum CashflowDirection { Receipt, Payment }
    private enum CashflowSource { PostedDocument, RecurringTemplate, PlannedPurchase, Scenario }
    private sealed record CashflowEvent(
        DateOnly Date,
        decimal Amount,
        CashflowDirection Direction,
        CashflowSource Source,
        Guid? SourceEntityId = null);
}
