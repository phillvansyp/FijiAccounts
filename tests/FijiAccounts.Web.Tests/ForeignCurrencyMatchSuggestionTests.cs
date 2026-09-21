using FijiAccounts.Web.Services;
namespace FijiAccounts.Web.Tests;
public sealed class ForeignCurrencyMatchSuggestionTests
{
    [Theory]
    [InlineData("NZD", 90, true)]
    [InlineData("NZD", 110, true)]
    [InlineData("NZD", 89.99, false)]
    [InlineData("NZD", 110.01, false)]
    [InlineData("FJD", 99, false)]
    [InlineData("NZD", -100, false)]
    public void OnlyForeignCurrencyWithinTenPercent(string currency, decimal amount, bool expected) =>
        Assert.Equal(expected, ForeignCurrencyMatchSuggestion.Matches(currency, "FJD", 100, amount, true, new(2026,6,15), new(2026,6,24)));
    [Fact]
    public void JunePaymentSuggestsJuneInvoiceButNotAugustInvoice()
    {
        Assert.True(ForeignCurrencyMatchSuggestion.Matches("NZD", "FJD", 36182.25m, 34610.78m, true, new(2026,6,15), new(2026,6,24)));
        Assert.False(ForeignCurrencyMatchSuggestion.Matches("NZD", "FJD", 34291.60m, 34610.78m, true, new(2026,8,28), new(2026,6,24)));
        Assert.True(ForeignCurrencyMatchSuggestion.Matches("NZD", "FJD", 100, -95, false, new(2026,6,15), new(2026,6,24)));
        Assert.False(ForeignCurrencyMatchSuggestion.Matches("NZD", "FJD", 0, 0, true, new(2026,6,15), new(2026,6,24)));
    }
}
