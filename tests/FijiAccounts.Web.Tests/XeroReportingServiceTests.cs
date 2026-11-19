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
    public void Matches_Xero_browser_balance_sheet_classification_without_changing_net_assets()
    {
        var report = new XeroReportResult("Xero Test", new Uri("https://reporting.xero.com"),
            null, "Balance Sheet", [], new XeroReportRow[]
            {
                new("Section", "Assets", [], 0),
                new("Section", "Bank", [], 1),
                new("Row", null, ["Business Term Lending-003", "-953823.38", "-1000000.00"], 2),
                new("SummaryRow", null, ["Total Bank", "-844305.08", "-680248.39"], 2),
                new("Section", "Current Assets", [], 1),
                new("SummaryRow", null, ["Total Current Assets", "1349572.50", "3797384.90"], 2),
                new("SummaryRow", null, ["Total Assets", "766709.76", "3348334.94"], 1),
                new("Section", "Liabilities", [], 0),
                new("Section", "Current Liabilities", [], 1),
                new("Row", null, ["JCR Visa's", "-11306.16", "4188.18"], 2),
                new("Row", null, ["Provision for Holiday Pay", "-1809098.60", "2810581.28"], 2),
                new("SummaryRow", null, ["Total Current Liabilities", "499036.81", "4401892.20"], 2),
                new("SummaryRow", null, ["Total Liabilities", "499037.21", "4401892.93"], 1),
                new("Row", null, ["Net Assets", "267672.55", "-1053557.99"], 0)
            }, DateTimeOffset.UtcNow, null);

        var displayed = XeroReportingService.MatchXeroBalanceSheet(report,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Business Term Lending-003", "JCR Visa's" });
        string[] Amount(string label) => displayed.Rows.Single(x => x.Cells.Count > 0 && x.Cells[0] == label)
            .Cells.Skip(1).ToArray();

        Assert.Equal(["120824.46", "319751.61"], Amount("Total Bank"));
        Assert.Equal(["1731839.30", "4348334.94"], Amount("Total Assets"));
        Assert.Equal(["1464166.35", "5401892.20"], Amount("Total Current Liabilities"));
        Assert.Equal(["1464166.75", "5401892.93"], Amount("Total Liabilities"));
        Assert.Equal(["267672.55", "-1053557.99"], Amount("Net Assets"));
        Assert.Equal(["953823.38", "1000000.00"], Amount("Business Term Lending-003"));
        Assert.Equal(["11306.16", ""], displayed.Rows.Single(x =>
            x.Cells.Count > 0 && x.Cells[0] == "JCR Visa's" && x.Cells[1] == "11306.16").Cells.Skip(1));
        Assert.Equal(["0.00", "4188.18"], displayed.Rows.Single(x =>
            x.Cells.Count > 0 && x.Cells[0] == "JCR Visa's" && x.Cells[1] == "0.00").Cells.Skip(1));
        Assert.Equal(["-1809098.60", "2810581.28"], Amount("Provision for Holiday Pay"));
        Assert.Equal(["766709.76", "3348334.94"], report.Rows.Single(x => x.Cells.Count > 0 &&
            x.Cells[0] == "Total Assets").Cells.Skip(1));
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
        Assert.Equal("-25.50", total[7]);
        Assert.Equal("104.50", total[8]);
    }

    [Fact]
    public void Ages_xero_invoices_in_months_back_from_the_report_date()
    {
        var report = XeroReportingService.RenderAgeing(
            [new("Customer", "Current", new(2026, 11, 18), 1m),
             new("Customer", "Due on the report date", new(2026, 11, 17), 2m),
             new("Customer", "Within one month", new(2026, 10, 18), 3m),
             new("Customer", "One month", new(2026, 10, 17), 4m),
             new("Customer", "Two months", new(2026, 9, 17), 5m),
             new("Customer", "Three months", new(2026, 8, 17), 6m),
             new("Customer", "Four months", new(2026, 7, 17), 7m)],
            new(2026, 11, 17));

        Assert.Equal(["Total", "1.00", "5.00", "4.00", "5.00", "6.00", "7.00", "28.00"],
            report[^1].Cells);
    }

    [Fact]
    public void Uses_the_credit_document_date_to_age_a_credit_without_a_due_date()
    {
        var row = new XeroMirrorRecord
        {
            DatasetKey = "sales-credits", ExternalId = "dated-credit", DisplayName = "Credit note",
            SourceHash = "", SourceJson = """
                {"Type":"ACCRECCREDIT","Status":"AUTHORISED","RemainingCredit":25.50,
                 "Date":"/Date(1782259200000+0000)/","CurrencyCode":"NZD","CreditNoteNumber":"CN-1",
                 "Contact":{"Name":"Customer A"}}
                """
        };
        var credit = XeroOpenBalanceService.TryReadCredit(row, "NZD", payables: false);
        Assert.NotNull(credit);
        Assert.Equal(new DateOnly(2026, 6, 24), credit.DueDate);
        var report = XeroReportingService.RenderAgeing([credit], new(2026, 11, 17));
        Assert.Equal(["Total", "0.00", "0.00", "0.00", "0.00", "0.00", "-25.50", "-25.50"],
            report[^1].Cells);
    }
}

