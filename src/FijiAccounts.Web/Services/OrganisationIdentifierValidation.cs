namespace FijiAccounts.Web.Services;

public static class OrganisationIdentifierValidation
{
    public static string? NormaliseTaxIdentifier(string countryCode, string? value)
    {
        var identifier = NormaliseOptional(value, 32, "tax identification number");
        if (identifier is not null &&
            string.Equals(countryCode, "NZ", StringComparison.OrdinalIgnoreCase) &&
            !IsValidIrdNumber(identifier))
        {
            throw new InvalidOperationException("Enter a valid New Zealand IRD number.");
        }

        return identifier;
    }

    public static string? NormaliseNzbn(string countryCode, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!string.Equals(countryCode, "NZ", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("An NZBN can only be recorded for a New Zealand organisation.");
        }

        var trimmed = value.Trim();
        if (trimmed.Any(character => !char.IsDigit(character) && character is not ' ' and not '-'))
        {
            throw new InvalidOperationException("Enter the NZBN as a 13-digit number.");
        }

        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        if (!IsValidNzbn(digits))
        {
            throw new InvalidOperationException("Enter a valid 13-digit NZBN.");
        }

        return digits;
    }

    public static bool IsValidIrdNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (value.Any(character => !char.IsDigit(character) && character is not ' ' and not '-'))
        {
            return false;
        }

        if (digits.Length == 8) digits = $"0{digits}";
        if (digits.Length != 9) return false;

        int CalculateCheckDigit(int[] weights)
        {
            var remainder = Enumerable.Range(0, 8)
                .Sum(index => (digits[index] - '0') * weights[index]) % 11;
            return remainder == 0 ? 0 : 11 - remainder;
        }

        var checkDigit = CalculateCheckDigit([3, 2, 7, 6, 5, 4, 3, 2]);
        if (checkDigit == 10)
        {
            checkDigit = CalculateCheckDigit([7, 4, 3, 2, 5, 2, 7, 6]);
        }

        return checkDigit is >= 0 and <= 9 && checkDigit == digits[8] - '0';
    }

    public static bool IsValidNzbn(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 13 ||
            value.Any(character => !char.IsDigit(character)) ||
            !value.StartsWith("94", StringComparison.Ordinal))
        {
            return false;
        }

        var weightedSum = 0;
        for (var index = 0; index < 12; index++)
        {
            weightedSum += (value[index] - '0') * (index % 2 == 0 ? 1 : 3);
        }

        var checkDigit = (10 - weightedSum % 10) % 10;
        return checkDigit == value[12] - '0';
    }

    private static string? NormaliseOptional(string? value, int maximumLength, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Length > maximumLength)
        {
            throw new InvalidOperationException($"The {label} must be {maximumLength} characters or fewer.");
        }

        return trimmed;
    }
}
