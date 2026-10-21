using FijiAccounts.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Pdf;

namespace FijiAccounts.Web.Tests;

public sealed class SupplierBillDocumentReaderTests
{
    [Fact]
    public async Task ReadPdfAsync_UsesOcrWhenPdfHasNoSearchableText()
    {
        var ocr = new StubPdfOcrEngine("Invoice 5685 Total due FJD 2,526.72");
        var reader = new SupplierBillDocumentReader(
            ocr,
            NullLogger<SupplierBillDocumentReader>.Instance);

        var result = await reader.ReadPdfAsync(CreateImageOnlyPdf());

        Assert.True(result.UsedOcr);
        Assert.True(result.OcrAttempted);
        Assert.Equal("Invoice 5685 Total due FJD 2,526.72", result.Text);
        Assert.Equal(1, ocr.CallCount);
    }

    [Fact]
    public async Task ReadPdfAsync_ReportsAttemptWhenOcrCannotReadEnoughText()
    {
        var ocr = new StubPdfOcrEngine("Unreadable");
        var reader = new SupplierBillDocumentReader(
            ocr,
            NullLogger<SupplierBillDocumentReader>.Instance);

        var result = await reader.ReadPdfAsync(CreateImageOnlyPdf());

        Assert.False(result.UsedOcr);
        Assert.True(result.OcrAttempted);
        Assert.Equal(1, ocr.CallCount);
    }

    [Theory]
    [InlineData("Page 1 of 1", false)]
    [InlineData("Invoice 5685 Total due FJD 2,526.72", true)]
    public void HasUsefulText_UsesMinimumCharacterThreshold(string text, bool expected)
    {
        Assert.Equal(expected, SupplierBillDocumentReader.HasUsefulText(text));
    }

    private static byte[] CreateImageOnlyPdf()
    {
        using var document = new PdfDocument();
        document.AddPage();
        using var stream = new MemoryStream();
        document.Save(stream, closeStream: false);
        return stream.ToArray();
    }

    private sealed class StubPdfOcrEngine(string text) : IPdfOcrEngine
    {
        public int CallCount { get; private set; }

        public Task<string> ReadAsync(byte[] content, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(text);
        }
    }
}
