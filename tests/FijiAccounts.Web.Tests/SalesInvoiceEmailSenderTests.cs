using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class SalesInvoiceEmailSenderTests
{
    [Fact]
    public void BuildMessage_AttachesPdfAndEscapesHtml()
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

        var pdf = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        var message = SalesInvoiceEmailSender.BuildMessage(invoice, "accounts@example.com", pdf);

        Assert.Equal("accounts@example.com", message.Recipient);
        Assert.Contains("INV-000003", message.Subject);
        Assert.Contains("NZD 26,047.90", message.TextBody);
        Assert.Contains("Client &lt;Accounts&gt;", message.HtmlBody);
        Assert.Contains("attached as a PDF", message.HtmlBody);
        Assert.DoesNotContain("Call Centre", message.HtmlBody);
        Assert.DoesNotContain("Client <Accounts>", message.HtmlBody);
        var attachment = Assert.Single(message.Files);
        Assert.Equal("INV-000003.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Same(pdf, attachment.Content);
    }

    [Fact]
    public void PdfRenderer_CreatesStandaloneInvoiceDocument()
    {
        var organisation = new Organisation
        {
            LegalName = "JCR 2006 Limited",
            BusinessAddress = "Level 3, Jetpoint,Nadi,Fiji",
            Tin = "2900933974",
            BaseCurrency = "FJD"
        };
        var customer = new BusinessParty
        {
            Organisation = organisation,
            Name = "1st Call Recruitment",
            Type = PartyType.Customer
        };
        var branch = new Branch
        {
            Organisation = organisation,
            OrganisationId = organisation.Id,
            Code = "MAIN",
            Name = "Main Branch"
        };
        var division = new Division
        {
            Branch = branch,
            BranchId = branch.Id,
            Code = "GENERAL",
            Name = "General"
        };
        var invoice = new SalesInvoice
        {
            Organisation = organisation,
            Branch = branch,
            Division = division,
            Customer = customer,
            CustomerId = customer.Id,
            InvoiceNumber = "INV-000003",
            IssueDate = new DateOnly(2026, 8, 28),
            DueDate = new DateOnly(2026, 9, 4),
            Currency = "NZD",
            ExchangeRateToBase = 1.31648236m,
            TransactionSubtotal = 26047.90m,
            TransactionTotal = 26047.90m,
            Total = 34291.60m,
            Status = InvoiceStatus.Posted,
            IsTaxInvoice = false,
            CreatedByUserId = "user",
            Lines =
            [
                new SalesInvoiceLine
                {
                    Description = "Call Centre",
                    Quantity = 1,
                    TransactionUnitPrice = 26047.90m,
                    TransactionNetAmount = 26047.90m,
                    VatTreatment = VatTreatment.ZeroRated
                }
            ]
        };

        var pdf = new SalesInvoicePdfRenderer().Render(invoice, branding: null);

        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);
        var text = string.Join(" ", document.GetPages().Select(page => page.Text));
        Assert.Contains("Commercial Invoice", text);
        Assert.Contains("INV-000003", text);
        Assert.Contains("Call Centre", text);
        Assert.Contains("Main Branch", text);
        Assert.Contains("General", text);
        Assert.Contains("Project", text);
        Assert.Contains("Customer PO", text);
        Assert.Contains("Zero-rated 0%", text);
        Assert.Contains("Powered by AccountIsland.com", text);
    }

    [Fact]
    public void PdfRenderer_FallsBackWhenBrandLogoIsInvalid()
    {
        var organisation = new Organisation
        {
            LegalName = "JCR 2006 Limited",
            BaseCurrency = "FJD"
        };
        var customer = new BusinessParty
        {
            Organisation = organisation,
            Name = "1st Call Recruitment",
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
            Status = InvoiceStatus.Posted,
            CreatedByUserId = "user"
        };
        var branding = new OrganisationBranding
        {
            Organisation = organisation,
            OrganisationId = organisation.Id,
            LogoFileName = "invalid.png",
            LogoContentType = "image/png",
            LogoContent = [0x01, 0x02, 0x03],
            UploadedByUserId = "user"
        };

        var pdf = new SalesInvoicePdfRenderer().Render(invoice, branding);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);
        Assert.Contains("JCR 2006 Limited", document.GetPage(1).Text);
    }

}
