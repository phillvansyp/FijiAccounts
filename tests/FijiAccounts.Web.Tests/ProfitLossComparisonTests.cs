using System.Reflection;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Components.Pages;
using FijiAccounts.Web.Services;
using Microsoft.JSInterop;

namespace FijiAccounts.Web.Tests;

public sealed class ProfitLossComparisonTests
{
    [Fact]
    public async Task ComparisonExport_AlignsBothMonthsAndIncludesPriorOnlyAccountsAndTotals()
    {
        var page = new Reports();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var period = typeof(Reports).GetField("period", flags)!.GetValue(page)!;
        void SetPeriod(string name, object value) => period.GetType().GetProperty(name)!.SetValue(period, value);
        void SetField(string name, object value) => typeof(Reports).GetField(name, flags)!.SetValue(page, value);
        SetPeriod("From", new DateOnly(2026, 8, 1));
        SetPeriod("To", new DateOnly(2026, 8, 31));
        SetPeriod("CompareWith", "previous-period");
        SetField("comparisonFrom", new DateOnly(2026, 7, 1));
        SetField("comparisonTo", new DateOnly(2026, 7, 31));
        SetField("report", new FinancialReportData([
            new("4000", "Sales", AccountType.Revenue, 200m),
            new("5000", "Rent", AccountType.Expense, 30m),
            new("6000", "Unused", AccountType.Expense, 0m)], []));
        SetField("comparisonColumns", new List<ReportComparisonColumn> { new(new(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)), new FinancialReportData([
            new("4000", "Sales", AccountType.Revenue, 100m),
            new("5100", "July expense", AccountType.Expense, 20m)], [])),
            new(new(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)), new FinancialReportData([
                new("5200", "June expense", AccountType.Expense, 10m)], [])) });
        var js = new CaptureJs();
        typeof(Reports).GetProperty("JS", flags)!.SetValue(page, js);

        await (Task)typeof(Reports).GetMethod("ExportCsv", flags)!.Invoke(page, null)!;

        Assert.Contains("\"August 2026\",\"July 2026\",\"June 2026\"", js.Csv);
        Assert.Contains("\"4000\",\"Sales\",Revenue,200,100,0", js.Csv);
        Assert.Contains("\"5000\",\"Rent\",Expense,30,0,0", js.Csv);
        Assert.Contains("\"5100\",\"July expense\",Expense,0,20,0", js.Csv);
        Assert.Contains("\"5200\",\"June expense\",Expense,0,0,10", js.Csv);
        Assert.DoesNotContain("Unused", js.Csv);
        Assert.Contains(",\"Total income\",,200,100,0", js.Csv);
        Assert.Contains(",\"Total expenses\",,30,20,10", js.Csv);
        Assert.Contains(",Net profit,,170,80,-10", js.Csv);
    }

    private sealed class CaptureJs : IJSRuntime
    {
        public string Csv { get; private set; } = "";
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Csv = (string)args![1]!;
            return ValueTask.FromResult(default(TValue)!);
        }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }
}
