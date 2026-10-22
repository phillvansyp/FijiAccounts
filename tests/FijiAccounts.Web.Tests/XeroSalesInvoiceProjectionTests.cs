using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class XeroSalesInvoiceProjectionTests
{
    [Fact]
    public void TryParse_ProducesReadOnlyInvoiceAmountsStatusAndLines()
    {
        var record = new XeroMirrorRecord
        {
            OrganisationId = Guid.NewGuid(),
            XeroConnectionId = Guid.NewGuid(),
            DatasetKey = "sales-invoices",
            ExternalId = "invoice-id",
            DisplayName = "INV-1042",
            SourceHash = new string('a', 64),
            SourceJson = """
                {
                  "InvoiceNumber":"INV-1042",
                  "Reference":"PO-77",
                  "Date":"2026-10-20",
                  "DueDate":"2026-11-20",
                  "Status":"AUTHORISED",
                  "CurrencyCode":"USD",
                  "CurrencyRate":1.5,
                  "SubTotal":100,
                  "TotalTax":15,
                  "Total":115,
                  "AmountDue":75,
                  "AmountPaid":40,
                  "AmountCredited":0,
                  "Contact":{"Name":"Example Customer"},
                  "LineItems":[{
                    "Description":"Consulting",
                    "Quantity":2,
                    "UnitAmount":50,
                    "LineAmount":100,
                    "TaxAmount":15,
                    "AccountCode":"200",
                    "TaxType":"OUTPUT2"
                  }]
                }
                """
        };

        var parsed = XeroSalesInvoiceProjectionParser.TryParse(record, out var invoice);

        Assert.True(parsed);
        Assert.Equal("INV-1042", invoice.InvoiceNumber);
        Assert.Equal("Example Customer", invoice.ContactName);
        Assert.Equal("PartPaid", invoice.StatusKey);
        Assert.Equal("Part paid", invoice.StatusLabel);
        Assert.Equal(172.50m, invoice.BaseTotal);
        Assert.Equal(112.50m, invoice.BaseAmountDue);
        Assert.Single(invoice.Lines);
        Assert.Equal("Consulting", invoice.Lines[0].Description);
        Assert.Equal(15m, invoice.Lines[0].TaxAmount);
    }
}
