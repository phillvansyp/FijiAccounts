using FijiAccounts.Web.Services;
using FijiAccounts.Domain.Tax;

namespace FijiAccounts.Web.Tests;

public sealed class SupplierBillDocumentFieldParserTests
{
    [Fact]
    public void CalculateExclusiveAmount_ConvertsFijiVatInclusiveTotal()
    {
        var result = SupplierBillDocumentFieldParser.CalculateExclusiveAmount(
            78.26m,
            new DateOnly(2026, 6, 24),
            "FJD",
            VatTreatment.Standard);

        Assert.Equal(69.56m, result);
    }

    [Fact]
    public void ExtractReference_DoesNotTreatCreditCodeAsInvoiceNumber()
    {
        const string text = "Tax Invoice CreditCode\nInvoice Date 24/06/2026";

        var result = SupplierBillDocumentFieldParser.ExtractReference(text, "92064234.pdf");

        Assert.Equal("92064234", result);
    }

    [Theory]
    [InlineData("Invoice Number: INV-12345", "scan.pdf", "INV-12345")]
    [InlineData("Tax Invoice No. 778899", "invoice.pdf", "778899")]
    [InlineData("Invoice: AB/2026/44", "document.pdf", "AB/2026/44")]
    public void ExtractReference_ReadsExplicitInvoiceLabels(
        string text,
        string fileName,
        string expected)
    {
        Assert.Equal(expected, SupplierBillDocumentFieldParser.ExtractReference(text, fileName));
    }
}
