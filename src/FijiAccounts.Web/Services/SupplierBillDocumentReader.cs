using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace FijiAccounts.Web.Services;

public sealed record SupplierBillDocumentText(string Text, bool UsedOcr, bool OcrAttempted);

public sealed class SupplierBillDocumentReader(
    IPdfOcrEngine ocr,
    ILogger<SupplierBillDocumentReader> logger)
{
    internal const int MinimumUsefulCharacters = 20;

    public async Task<SupplierBillDocumentText> ReadPdfAsync(
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        var text = ExtractEmbeddedText(content);
        if (HasUsefulText(text))
        {
            return new SupplierBillDocumentText(text, UsedOcr: false, OcrAttempted: false);
        }

        try
        {
            var ocrText = await ocr.ReadAsync(content, cancellationToken);
            if (HasUsefulText(ocrText))
            {
                return new SupplierBillDocumentText(ocrText, UsedOcr: true, OcrAttempted: true);
            }

            return new SupplierBillDocumentText(text, UsedOcr: false, OcrAttempted: true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Supplier bill OCR exceeded its processing time limit.");
            return new SupplierBillDocumentText(text, UsedOcr: false, OcrAttempted: true);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or Win32Exception)
        {
            logger.LogWarning(ex, "Supplier bill OCR was unavailable or could not read the PDF.");
            return new SupplierBillDocumentText(text, UsedOcr: false, OcrAttempted: true);
        }
    }

    internal static bool HasUsefulText(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        text.Count(char.IsLetterOrDigit) >= MinimumUsefulCharacters;

    private static string ExtractEmbeddedText(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var document = PdfDocument.Open(stream);
        var extracted = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            extracted.AppendLine(ContentOrderTextExtractor.GetText(page));
        }

        return extracted.ToString();
    }
}

public interface IPdfOcrEngine
{
    Task<string> ReadAsync(byte[] content, CancellationToken cancellationToken);
}

internal sealed class LocalPdfOcrEngine(ILogger<LocalPdfOcrEngine> logger) : IPdfOcrEngine
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(45);
    private const int MaximumPages = 5;

    public async Task<string> ReadAsync(byte[] content, CancellationToken cancellationToken)
    {
        using var overallTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        overallTimeout.CancelAfter(ProcessTimeout);
        var processingToken = overallTimeout.Token;
        var workDirectory = Path.Combine(Path.GetTempPath(), $"account-island-ocr-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDirectory);

        try
        {
            var pdfPath = Path.Combine(workDirectory, "document.pdf");
            var pagePrefix = Path.Combine(workDirectory, "page");
            await File.WriteAllBytesAsync(pdfPath, content, processingToken);

            var render = await RunAsync(
                "pdftoppm",
                ["-png", "-gray", "-r", "220", "-f", "1", "-l", MaximumPages.ToString(), pdfPath, pagePrefix],
                processingToken);
            if (render.ExitCode != 0)
            {
                throw new InvalidDataException($"PDF rendering failed: {render.Error}");
            }

            var pages = Directory.GetFiles(workDirectory, "page-*.png")
                .OrderBy(PageNumber)
                .ToList();
            if (pages.Count == 0)
            {
                throw new InvalidDataException("PDF rendering produced no pages.");
            }

            var text = new StringBuilder();
            foreach (var page in pages)
            {
                var result = await RunAsync(
                    "tesseract",
                    [page, "stdout", "-l", "eng", "--psm", "6"],
                    processingToken);
                if (result.ExitCode != 0)
                {
                    logger.LogWarning("Tesseract could not read {Page}: {Error}", Path.GetFileName(page), result.Error);
                    continue;
                }

                text.AppendLine(result.Output);
            }

            return text.ToString();
        }
        finally
        {
            try
            {
                if (Directory.Exists(workDirectory)) Directory.Delete(workDirectory, recursive: true);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Could not remove temporary supplier bill OCR files.");
            }
        }
    }

    private static int PageNumber(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var separator = name.LastIndexOf('-');
        return separator >= 0 && int.TryParse(name[(separator + 1)..], out var page) ? page : int.MaxValue;
    }

    private static async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProcessTimeout);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["OMP_THREAD_LIMIT"] = "2";
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException($"Could not start {fileName}.");

        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
