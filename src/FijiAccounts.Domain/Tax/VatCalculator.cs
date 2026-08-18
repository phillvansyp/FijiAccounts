using FijiAccounts.Domain.Accounting;

namespace FijiAccounts.Domain.Tax;

public enum VatTreatment { Standard, ZeroRated, Exempt, OutOfScope }

public sealed record VatRate(DateOnly EffectiveFrom, DateOnly? EffectiveTo, decimal Rate);

public sealed class FijiVatSchedule
{
    // Effective-dated historical rates. Classification of supplies is a separate concern.
    private static readonly VatRate[] Rates =
    [
        new(new DateOnly(2016, 1, 1), new DateOnly(2023, 7, 31), 0.09m),
        new(new DateOnly(2023, 8, 1), new DateOnly(2025, 7, 31), 0.15m),
        new(new DateOnly(2025, 8, 1), null, 0.125m)
    ];

    public decimal StandardRateOn(DateOnly date) => Rates.SingleOrDefault(x =>
        date >= x.EffectiveFrom && (x.EffectiveTo is null || date <= x.EffectiveTo))?.Rate
        ?? throw new DomainException($"No reviewed Fiji VAT rate exists for {date:yyyy-MM-dd}.");

    public VatResult CalculateFromExclusive(Money exclusive, DateOnly date, VatTreatment treatment)
    {
        var rate = treatment == VatTreatment.Standard ? StandardRateOn(date) : 0m;
        var vat = new Money(exclusive.Amount * rate, exclusive.Currency).Round();
        return new(exclusive.Round(), vat, new Money(exclusive.Amount + vat.Amount, exclusive.Currency).Round(), rate, treatment);
    }

    public VatResult CalculateFromInclusive(Money inclusive, DateOnly date, VatTreatment treatment)
    {
        var roundedInclusive = inclusive.Round();
        var rate = treatment == VatTreatment.Standard ? StandardRateOn(date) : 0m;
        var exclusive = new Money(roundedInclusive.Amount / (1m + rate), inclusive.Currency).Round();
        var vat = new Money(roundedInclusive.Amount - exclusive.Amount, inclusive.Currency).Round();
        return new(exclusive, vat, roundedInclusive, rate, treatment);
    }
}

public sealed record VatResult(Money Exclusive, Money Vat, Money Inclusive, decimal Rate, VatTreatment Treatment);
