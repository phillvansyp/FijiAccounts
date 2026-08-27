namespace FijiAccounts.Web.Services;

public sealed record ForeignCurrencySettlement(
    decimal TransactionAmount,
    decimal DocumentBaseAmount,
    decimal ActualBaseAmount,
    decimal SettlementRateToBase,
    decimal RealisedExchangeDifference)
{
    public static ForeignCurrencySettlement Calculate(
        decimal transactionAmount,
        decimal documentRateToBase,
        decimal actualBaseAmount)
    {
        if (transactionAmount <= 0 || documentRateToBase <= 0 || actualBaseAmount <= 0)
        {
            throw new InvalidOperationException("Settlement amounts and exchange rates must be greater than zero.");
        }

        var documentBaseAmount = decimal.Round(
            transactionAmount * documentRateToBase,
            2,
            MidpointRounding.AwayFromZero);
        return new(
            transactionAmount,
            documentBaseAmount,
            actualBaseAmount,
            decimal.Round(actualBaseAmount / transactionAmount, 8, MidpointRounding.AwayFromZero),
            actualBaseAmount - documentBaseAmount);
    }
}
