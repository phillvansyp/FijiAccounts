namespace FijiAccounts.Web.Services;

public static class ForeignCurrencyMatchSuggestion
{
    public static bool Matches(string currency, string baseCurrency, decimal outstanding,
        decimal bankAmount, bool isSales, DateOnly documentDate, DateOnly paymentDate) =>
        !string.IsNullOrWhiteSpace(currency) && !string.IsNullOrWhiteSpace(baseCurrency) &&
        !string.Equals(currency.Trim(), baseCurrency.Trim(), StringComparison.OrdinalIgnoreCase) &&
        outstanding > 0 && (isSales ? bankAmount > 0 : bankAmount < 0) &&
        documentDate <= paymentDate.AddDays(3) &&
        Math.Abs(Math.Abs(bankAmount) - outstanding) <= outstanding * 0.10m;
}
