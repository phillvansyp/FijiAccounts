namespace FijiAccounts.Web.Services;

public sealed class ReportComparisonOptions
{
    public int Count { get; set; } = 1;
    public string Unit { get; set; } = "month";
    public DateOnly CustomFrom { get; set; }
    public DateOnly CustomTo { get; set; }
    public string Label => Count == 0 ? "None" : Unit == "custom" ? "Custom date range" : $"{Count} {Unit}{(Count == 1 ? "" : "s")}";

    public IReadOnlyList<ReportComparisonPeriod> Periods(DateOnly from, DateOnly to)
    {
        if (Count is < 0 or > 60) throw new ArgumentException("Choose between 0 and 60 comparison periods.");
        if (from > to) throw new ArgumentException("The From date must be on or before the To date.");
        if (Count == 0) return [];
        if (Unit == "custom")
        {
            if (CustomFrom == default || CustomTo == default || CustomFrom > CustomTo)
                throw new ArgumentException("Choose a valid custom comparison date range.");
            return [new(CustomFrom, CustomTo)];
        }

        var months = Unit switch { "month" => 1, "quarter" => 3, "year" => 12, _ => throw new ArgumentException("Choose Month, Quarter, or Year.") };
        var endOfMonth = to.Day == DateTime.DaysInMonth(to.Year, to.Month);
        return Enumerable.Range(1, Count).Select(index =>
        {
            var start = from.AddMonths(-months * index);
            var end = to.AddMonths(-months * index);
            if (endOfMonth) end = new DateOnly(end.Year, end.Month, DateTime.DaysInMonth(end.Year, end.Month));
            return new ReportComparisonPeriod(start, end);
        }).ToArray();
    }
}

public sealed record ReportComparisonPeriod(DateOnly From, DateOnly To);
public sealed record ReportComparisonColumn(ReportComparisonPeriod Period, FinancialReportData Report);
