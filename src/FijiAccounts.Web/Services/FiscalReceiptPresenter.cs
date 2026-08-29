using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record FiscalReceiptView(
    string SdcInvoiceNumber,
    DateTimeOffset IssuedAt,
    string? VerificationUrl,
    string? QrImageSource,
    bool IsSimulated);

public static class FiscalReceiptPresenter
{
    private const int MaximumQrImageLength = 1_500_000;

    public static FiscalReceiptView Create(FiscalisationRecord record)
    {
        if (record.Status != FiscalisationStatus.Accepted ||
            string.IsNullOrWhiteSpace(record.SdcInvoiceNumber) ||
            record.SdcIssuedAt is null)
        {
            throw new InvalidOperationException(
                "Only an accepted fiscal response can be shown on an invoice.");
        }

        var simulated = record.SdcInvoiceNumber.StartsWith(
            "SIMULATED-",
            StringComparison.OrdinalIgnoreCase);
        return new FiscalReceiptView(
            record.SdcInvoiceNumber,
            record.SdcIssuedAt.Value,
            simulated ? null : SafeVerificationUrl(record.VerificationUrl),
            simulated ? null : SafeQrImage(record.VerificationQrCode),
            simulated);
    }

    private static string? SafeVerificationUrl(string? value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Scheme is "https" or "http")
        {
            return uri.AbsoluteUri;
        }
        return null;
    }

    private static string? SafeQrImage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumQrImageLength)
        {
            return null;
        }

        return value.StartsWith("data:image/png;base64,", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("data:image/jpeg;base64,", StringComparison.OrdinalIgnoreCase)
            ? value
            : null;
    }
}
