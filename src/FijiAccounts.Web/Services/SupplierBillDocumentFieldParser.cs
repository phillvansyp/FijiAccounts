using System.Globalization;
using System.Text.RegularExpressions;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;

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

    public static string? MatchSupplierName(
        string text,
        IEnumerable<string> supplierNames)
    {
        var normalizedText = Normalize(text);
        var names = supplierNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var exact = names
            .Select(name => new { Name = name, Normalized = Normalize(name) })
            .Where(x => ContainsPhrase(normalizedText, x.Normalized))
            .OrderByDescending(x => x.Normalized.Length)
            .FirstOrDefault();
        if (exact is not null)
        {
            return exact.Name;
        }

        var leadingTokens = names
            .Select(name => new
            {
                Name = name,
                Token = Normalize(name).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            })
            .Where(x => x.Token is { Length: >= 3 })
            .ToList();

        return leadingTokens
            .Where(candidate =>
                leadingTokens.Count(x =>
                    string.Equals(x.Token, candidate.Token, StringComparison.Ordinal)) == 1 &&
                ContainsPhrase(normalizedText, candidate.Token!))
            .OrderByDescending(x => x.Token!.Length)
            .Select(x => x.Name)
            .FirstOrDefault();
    }

    public static string? ExtractDescription(string text)
    {
        var match = DescriptionPattern().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var description = Regex.Replace(match.Groups[1].Value, @"\s+", " ")
            .Trim()
            .TrimEnd(':');
        return description.Length is > 0 and <= 300 ? description : null;
    }

    public static bool TryExtractPaymentTerms(string text, out int days)
    {
        days = 0;
        if (Regex.IsMatch(text, @"(?im)\b(?:due\s+and\s+payable|payment\s+due)\s+upon\s+receipt\b"))
        {
            return true;
        }

        var match = Regex.Match(text, @"(?im)(?:payment\s+terms?|terms?)\s*[:\-]?\s*(?:net\s*)?(\d{1,3})\s*days?");
        if (!match.Success)
        {
            match = Regex.Match(text, @"(?im)due\s+(?:within|in)\s+(\d{1,3})\s*days?");
        }

        return match.Success &&
               int.TryParse(match.Groups[1].Value, out days) &&
               days is >= 0 and <= 365;
    }

    public static bool TryExtractMoney(
        string text,
        string labelPattern,
        out decimal value)
    {
        value = 0;
        const string amount = @"(?:FJD\s*|\$\s*)?([0-9][0-9,]*\.\d{2})";
        var match = Regex.Match(text, $@"(?im){labelPattern}[^\r\n0-9]{{0,80}}{amount}");
        if (!match.Success)
        {
            match = Regex.Match(text, $@"(?im){amount}[^\r\nA-Za-z]{{0,20}}{labelPattern}");
        }

        if (!match.Success && TryExtractStackedSummaryAmount(text, labelPattern, out value))
        {
            return true;
        }

        if (!match.Success)
        {
            var collapsed = Regex.Replace(text, @"\s+", " ");
            match = Regex.Match(collapsed, $@"(?i){labelPattern}[^0-9]{{0,80}}?{amount}");
        }

        return match.Success && decimal.TryParse(
            match.Groups[1].Value.Replace(",", ""),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out value);
    }

    public static decimal CalculateExclusiveAmount(
        decimal gross,
        DateOnly billDate,
        string currency,
        VatTreatment vatTreatment,
        string countryCode = "FJ") =>
        IndirectTaxSchedules.For(countryCode)
            .CalculateFromInclusive(
                new Money(gross, currency),
                billDate,
                vatTreatment)
            .Exclusive.Amount;

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
        yield return ReferenceNumberPattern();
    }

    private static string Normalize(string value) =>
        Regex.Replace(value.ToUpperInvariant(), @"[^A-Z0-9]+", " ").Trim();

    private static bool ContainsPhrase(string text, string phrase) =>
        phrase.Length > 0 &&
        Regex.IsMatch(text, $@"(?:^|\s){Regex.Escape(phrase)}(?:\s|$)");

    private static bool TryExtractStackedSummaryAmount(
        string text,
        string labelPattern,
        out decimal value)
    {
        value = 0;
        var lines = text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length > 0)
            .ToList();
        const string summaryLabel = @"(?i)\b(?:sub\s*total|(?:total\s+)?vat(?:\s+amount)?|tax(?:\s+(?:total|amount))?|balance\s+due|amount\s+due|total\s+due|grand\s+total|invoice\s+(?:total|amount))\b";
        var targetIndexes = Enumerable.Range(0, lines.Count)
            .Where(i => Regex.IsMatch(lines[i], labelPattern, RegexOptions.IgnoreCase));
        foreach (var targetIndex in targetIndexes)
        {
            var start = targetIndex;
            while (start > 0 && Regex.IsMatch(lines[start - 1], summaryLabel))
            {
                start--;
            }

            var labels = new List<int>();
            var cursor = start;
            while (cursor < lines.Count && cursor <= targetIndex + 6)
            {
                if (Regex.IsMatch(lines[cursor], summaryLabel))
                {
                    labels.Add(cursor);
                    cursor++;
                    continue;
                }

                break;
            }

            var ordinal = labels.IndexOf(targetIndex);
            if (ordinal < 0 || labels.Count == 0)
            {
                continue;
            }

            var amounts = new List<decimal>();
            for (var i = cursor; i < lines.Count && i < cursor + labels.Count + 4; i++)
            {
                var amountMatch = Regex.Match(
                    lines[i],
                    @"(?i)(?:FJD\s*|\$\s*)?([0-9][0-9,]*\.\d{2})");
                if (amountMatch.Success && decimal.TryParse(
                        amountMatch.Groups[1].Value.Replace(",", ""),
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var parsed))
                {
                    amounts.Add(parsed);
                }
            }

            if (ordinal < amounts.Count)
            {
                value = amounts[ordinal];
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"(?im)\b(?:tax\s+)?invoice\s*(?:number|no\.?|#)\s*[:\-]?\s*([A-Z0-9][A-Z0-9\-/]{2,})")]
    private static partial Regex InvoiceNumberPattern();

    [GeneratedRegex(@"(?im)^\s*(?:tax\s+)?invoice\s*[:#\-]\s*([A-Z0-9][A-Z0-9\-/]{2,})\s*$")]
    private static partial Regex InvoiceWithSeparatorPattern();

    [GeneratedRegex(@"(?im)\breference\s*(?:number|no\.?|#)\s*[:\-]?\s*([A-Z0-9][A-Z0-9\-/]{2,})")]
    private static partial Regex ReferenceNumberPattern();

    [GeneratedRegex(@"(?ims)^\s*Re:\s*(.+?)(?=\r?\n\s*(?:Our\s+fees|Invoice\s+details|Description)\b|\r?\n\s*\r?\n|\z)")]
    private static partial Regex DescriptionPattern();
}
