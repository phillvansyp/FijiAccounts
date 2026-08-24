using System.Text.RegularExpressions;

namespace FijiAccounts.Web.Services;

public static partial class SupplierBillDocumentFieldParser
{
    private static readonly HashSet<string> RejectedReferences = new(StringComparer.OrdinalIgnoreCase)
    {
        "ACCOUNT",
        "CODE",
        "CREDIT",
        "CREDITCODE",
        "CUSTOMER",
        "DATE",
        "INVOICE",
        "NO",
        "NUMBER",
        "TAX"
    };

    public static string? ExtractReference(string text, string fileName)
    {
        foreach (var pattern in ReferencePatterns())
        {
            var match = pattern.Match(text);
            if (match.Success && IsPlausible(match.Groups[1].Value))
            {
                return match.Groups[1].Value.Trim();
            }
        }

        var fileStem = Path.GetFileNameWithoutExtension(fileName).Trim();
        return IsPlausible(fileStem) && fileStem.Any(char.IsDigit)
            ? fileStem
            : null;
    }

    private static bool IsPlausible(string value)
    {
        var candidate = value.Trim();
        return candidate.Length is >= 3 and <= 80 &&
               !RejectedReferences.Contains(candidate) &&
               candidate.Any(char.IsDigit) &&
               candidate.All(x => char.IsLetterOrDigit(x) || x is '-' or '/');
    }

    private static IEnumerable<Regex> ReferencePatterns()
    {
        yield return InvoiceNumberPattern();
        yield return InvoiceWithSeparatorPattern();
    }

    [GeneratedRegex(@"(?im)\b(?:tax\s+)?invoice\s*(?:number|no\.?|#)\s*[:\-]?\s*([A-Z0-9][A-Z0-9\-/]{2,})")]
    private static partial Regex InvoiceNumberPattern();

    [GeneratedRegex(@"(?im)^\s*(?:tax\s+)?invoice\s*[:#\-]\s*([A-Z0-9][A-Z0-9\-/]{2,})\s*$")]
    private static partial Regex InvoiceWithSeparatorPattern();
}
