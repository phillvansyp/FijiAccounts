using System.Text.RegularExpressions;

namespace FijiAccounts.Web.Services;

public static class BankDocumentReferenceMatcher
{
    public static bool Matches(string? documentReference, string? description, string? bankReference)
    {
        var reference = documentReference?.Trim();
        if (string.IsNullOrEmpty(reference)) return false;
        if (string.Equals(reference, bankReference?.Trim(), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(reference, description?.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        // Embedded references must contain a number; supplier names alone are not document identities.
        if (reference.Length < 5 || !reference.Any(char.IsDigit)) return false;
        var boundary = reference.All(char.IsDigit) ? "[0-9]" : "[A-Za-z0-9]";
        var pattern = "(?<!" + boundary + ")" + Regex.Escape(reference) + "(?!" + boundary + ")";
        return Regex.IsMatch(description ?? "", pattern, RegexOptions.IgnoreCase) ||
               Regex.IsMatch(bankReference ?? "", pattern, RegexOptions.IgnoreCase);
    }
}
