using FijiAccounts.Web.Services;
using FijiAccounts.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace FijiAccounts.Web.Tests;

public sealed class ReportComparisonOptionsTests
{
    [Fact]
    public void ThreeMonths_ProducesJulyJuneMayInOrder()
    {
        var periods = new ReportComparisonOptions { Count = 3 }.Periods(new(2026, 8, 1), new(2026, 8, 31));
        Assert.Equal(new[] { new ReportComparisonPeriod(new(2026, 7, 1), new(2026, 7, 31)), new(new(2026, 6, 1), new(2026, 6, 30)), new(new(2026, 5, 1), new(2026, 5, 31)) }, periods);
    }

    [Theory]
    [InlineData("month", 2026, 7)]
    [InlineData("quarter", 2026, 5)]
    [InlineData("year", 2025, 8)]
    public void Unit_SelectsCorrespondingEarlierPeriod(string unit, int year, int month)
    {
        var period = Assert.Single(new ReportComparisonOptions { Unit = unit }.Periods(new(2026, 8, 1), new(2026, 8, 31)));
        Assert.Equal(new DateOnly(year, month, 1), period.From);
        Assert.Equal(new DateOnly(year, month, 31), period.To);
    }

    [Fact]
    public void LeapYearAndUnequalMonths_KeepFullCalendarMonths()
    {
        var periods = new ReportComparisonOptions { Count = 2 }.Periods(new(2024, 3, 1), new(2024, 3, 31));
        Assert.Equal(new DateOnly(2024, 2, 29), periods[0].To);
        Assert.Equal(new DateOnly(2024, 1, 31), periods[1].To);
        var previous = Assert.Single(new ReportComparisonOptions().Periods(new(2026, 2, 1), new(2026, 2, 28)));
        Assert.Equal(new DateOnly(2026, 1, 31), previous.To);
    }

    [Fact]
    public void NoneAndCustomCount_RespectColumnCounts()
    {
        Assert.Empty(new ReportComparisonOptions { Count = 0 }.Periods(new(2026, 8, 1), new(2026, 8, 31)));
        Assert.Equal(12, new ReportComparisonOptions { Count = 12 }.Periods(new(2026, 8, 1), new(2026, 8, 31)).Count);
        Assert.Throws<ArgumentException>(() => new ReportComparisonOptions { Count = 61 }.Periods(new(2026, 8, 1), new(2026, 8, 31)));
    }

    [Fact]
    public void CustomRange_UsesExactDatesAndRejectsReversedDates()
    {
        var options = new ReportComparisonOptions { Unit = "custom", CustomFrom = new(2025, 6, 15), CustomTo = new(2025, 7, 20) };
        Assert.Equal(new ReportComparisonPeriod(options.CustomFrom, options.CustomTo), Assert.Single(options.Periods(new(2026, 8, 1), new(2026, 8, 31))));
        options.CustomTo = options.CustomFrom.AddDays(-1);
        Assert.Throws<ArgumentException>(() => options.Periods(new(2026, 8, 1), new(2026, 8, 31)));
    }

    [Fact]
    public async Task OpenPicker_RendersRequestedChoicesAndSelection()
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<OpenPicker>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Options"] = new ReportComparisonOptions { Count = 3 } }));
            return output.ToHtmlString();
        });
        foreach (var text in new[] { "None", "1 month", "2 months", "3 months", "4 months", "Enter a different number", "Month", "Quarter", "Year", "Custom date range" }) Assert.Contains(text, html);
        Assert.Contains("aria-expanded=\"true\"", html);
    }

    public sealed class OpenPicker : ReportComparisonPicker
    {
        protected override void OnInitialized() => typeof(ReportComparisonPicker).GetField("isOpen", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(this, true);
    }
}
