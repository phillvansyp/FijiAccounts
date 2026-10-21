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

    [Fact]
    public void ExtractReference_ReadsReferenceNumberLabel()
    {
        const string text = "JCR 2006 Limited Reference No. 464-10-26";

        Assert.Equal(
            "464-10-26",
            SupplierBillDocumentFieldParser.ExtractReference(text, "JCR 2006 YEAR END.pdf"));
    }

    [Fact]
    public void MatchSupplierName_UsesWholeWordsAndDistinctiveLeadingToken()
    {
        const string text = "PKF aliz pacific consultation with management and Directors";
        string[] suppliers = ["LTA", "PKF Accountants", "BOSS PTE Ltd"];

        Assert.Equal(
            "PKF Accountants",
            SupplierBillDocumentFieldParser.MatchSupplierName(text, suppliers));
    }

    [Fact]
    public void ExtractDescription_ReadsWrappedReLine()
    {
        const string text = "Re: Annual Accounting, Company Tax, and Corporate\n" +
                            "Secretarial for the year ended 31 March 2026:\n" +
                            "Our fees for the Annual Accounting work";

        Assert.Equal(
            "Annual Accounting, Company Tax, and Corporate Secretarial for the year ended 31 March 2026",
            SupplierBillDocumentFieldParser.ExtractDescription(text));
    }

    [Fact]
    public void TryExtractMoney_MapsStackedSummaryLabelsToTheirAmounts()
    {
        const string text = "Subtotal\nVat amount\nBalance Due and payable\n12.5\n6,155.56\n769.44\n$6,925.00";

        Assert.True(SupplierBillDocumentFieldParser.TryExtractMoney(text, @"sub\s*total", out var net));
        Assert.True(SupplierBillDocumentFieldParser.TryExtractMoney(text, @"vat\s+amount", out var vat));
        Assert.True(SupplierBillDocumentFieldParser.TryExtractMoney(text, @"balance\s+due", out var gross));
        Assert.Equal(6155.56m, net);
        Assert.Equal(769.44m, vat);
        Assert.Equal(6925m, gross);
    }

    [Fact]
    public void TryExtractPaymentTerms_RecognisesDueUponReceipt()
    {
        Assert.True(SupplierBillDocumentFieldParser.TryExtractPaymentTerms(
            "This bill of cost is due and payable upon receipt.",
            out var days));
        Assert.Equal(0, days);
    }
}
