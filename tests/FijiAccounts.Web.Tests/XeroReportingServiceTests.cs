using System.Text;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class XeroReportingServiceTests
{
    [Fact]
    public void Keeps_nested_Xero_report_totals_and_their_signs()
    {
        var result = XeroReportingService.ParseReport(Encoding.UTF8.GetBytes("""
            {"Reports":[{"ReportName":"Profit and Loss","ReportTitles":["Oct 2026"],"Rows":[
              {"RowType":"Header","Cells":[{"Value":""},{"Value":"Oct 2026"}]},
              {"RowType":"Section","Title":"Operating expenses","Rows":[
                {"RowType":"Row","Cells":[{"Value":"Adjustment"},{"Value":"-1234.56"}]},
                {"RowType":"SummaryRow","Cells":[{"Value":"Total Operating Expenses"},{"Value":"-1234.56"}]}
              ]},
              {"RowType":"Row","Cells":[{"Value":"Net Profit"},{"Value":"6789.01"}]}
            ]}]}
            """), "Xero Test", new Uri("https://reporting.xero.com"), "PAYMENTS", DateTimeOffset.UtcNow);
        Assert.Equal(["Header", "Section", "Row", "SummaryRow", "Row"], result.Rows.Select(x => x.Kind));
        Assert.Equal("-1234.56", result.Rows[3].Cells[1]);
        Assert.Equal("6789.01", result.Rows[4].Cells[1]);
        Assert.Equal("PAYMENTS", result.TaxBasis);
    }

    [Fact]
    public void Rejects_an_empty_or_malformed_Xero_response()
    {
        Assert.Throws<System.Text.Json.JsonException>(() => XeroReportingService.ParseReport(
            Encoding.UTF8.GetBytes("""{"Reports":[]}"""),
            "Xero Test", new Uri("https://reporting.xero.com"), null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Builds_the_accrual_report_query_for_the_selected_period()
    {
        var from = new DateOnly(2026, 10, 1);
        var to = new DateOnly(2026, 10, 31);
        Assert.Equal("Reports/ProfitAndLoss?fromDate=2026-10-01&toDate=2026-10-31&paymentsOnly=false",
            XeroReportingService.Endpoint("profit-loss", from, to));
        Assert.Equal("Reports/TrialBalance?date=2026-10-31&paymentsOnly=false",
            XeroReportingService.Endpoint("trial-balance", from, to));
        Assert.Equal("Reports", XeroReportingService.Endpoint("gst", from, to));
        Assert.Equal("Reports/BudgetSummary?date=2026-10-31&periods=12&timeframe=1",
            XeroReportingService.Endpoint("budget", from, to));
    }

    [Fact]
    public void Selects_a_finalised_GST_return_only_for_an_exact_period()
    {
        var json = Encoding.UTF8.GetBytes("""
            {"Reports":[
              {"ReportID":"1234","ReportName":"GST and Provisional Tax Return","ReportDate":"01 Oct 2026 to 31 Oct 2026"},
              {"ReportID":"1235","ReportName":"GST Return","ReportDate":"1 Sep 2026 to 30 Sep 2026"},
              {"ReportID":"../invalid","ReportName":"GST Return","ReportDate":"01 Oct 2026 to 31 Oct 2026"}
            ]}
            """);
        Assert.Equal("1234", XeroReportingService.FindFinalisedGstReport(
            json, new(2026, 10, 1), new(2026, 10, 31)));
        Assert.Null(XeroReportingService.FindFinalisedGstReport(
            json, new(2026, 10, 1), new(2026, 11, 30)));
    }

    [Fact]
    public void Includes_unapplied_receivable_credits_in_ageing_total()
    {
        var asAt = new DateOnly(2026, 11, 17);
        var row = new XeroMirrorRecord
        {
            DatasetKey = "overpayments", ExternalId = "example", DisplayName = "Overpayment",
            SourceHash = "", SourceJson = """
                {"Type":"RECEIVE-OVERPAYMENT","Status":"AUTHORISED","RemainingCredit":25.50,
                 "CurrencyCode":"NZD","Reference":"OP-1","Contact":{"Name":"Customer A"}}
                """
        };
        var credit = XeroOpenBalanceService.TryReadCredit(row, "NZD", payables: false);
        Assert.NotNull(credit);
        Assert.Null(XeroOpenBalanceService.TryReadCredit(row, "NZD", payables: true));
        var ageing = XeroReportingService.RenderAgeing(
            [new("Customer A", "INV-1", asAt.AddDays(-2), 100m),
             new("Customer B", "INV-2", asAt.AddDays(2), 30m), credit], asAt);
        var total = ageing[^1].Cells;
        Assert.Equal("30.00", total[1]);
        Assert.Equal("100.00", total[2]);
        Assert.Equal("-25.50", total[6]);
        Assert.Equal("104.50", total[7]);
    }
}
