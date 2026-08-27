using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class SalesInvoiceEmailSenderTests
{
    [Fact]
    public void BuildMessage_IncludesInvoiceDetailsAndEscapesHtml()
    {
        var organisation = new Organisation { LegalName = "JCR & Co" };
        var customer = new BusinessParty
        {
            Organisation = organisation,
            Name = "Client <Accounts>",
            Type = PartyType.Customer
        };
        var invoice = new SalesInvoice
        {
            Organisation = organisation,
            Customer = customer,
            CustomerId = customer.Id,
            InvoiceNumber = "INV-000003",
            IssueDate = new DateOnly(2026, 8, 28),
            DueDate = new DateOnly(2026, 9, 4),
            Currency = "NZD",
            TransactionTotal = 26047.90m,
            Status = InvoiceStatus.Posted,
            CreatedByUserId = "user",
            Lines =
            [
                new SalesInvoiceLine
                {
                    Description = "Call Centre <August>",
                    Quantity = 1,
                    TransactionNetAmount = 26047.90m,
                    VatTreatment = VatTreatment.Exempt
                }
            ]
        };

        var message = SalesInvoiceEmailSender.BuildMessage(invoice, "accounts@example.com");

        Assert.Equal("accounts@example.com", message.Recipient);
        Assert.Contains("INV-000003", message.Subject);
        Assert.Contains("NZD 26,047.90", message.TextBody);
        Assert.Contains("Client &lt;Accounts&gt;", message.HtmlBody);
        Assert.Contains("Call Centre &lt;August&gt;", message.HtmlBody);
        Assert.DoesNotContain("Client <Accounts>", message.HtmlBody);
    }
}
