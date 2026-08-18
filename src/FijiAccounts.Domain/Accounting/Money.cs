namespace FijiAccounts.Domain.Accounting;

public readonly record struct Money(decimal Amount, string Currency = "FJD")
{
    public Money Round() => new(decimal.Round(Amount, 2, MidpointRounding.AwayFromZero), Currency);
}
