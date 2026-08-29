using FijiAccounts.Domain.Accounting;

namespace FijiAccounts.Domain.Tax;

public enum VatTreatment { Standard, ZeroRated, Exempt, OutOfScope }

public sealed record VatRate(DateOnly EffectiveFrom, DateOnly? EffectiveTo, decimal Rate);

public interface IIndirectTaxSchedule
{
    decimal StandardRateOn(DateOnly date);
    VatResult CalculateFromExclusive(Money exclusive, DateOnly date, VatTreatment treatment);
    VatResult CalculateFromInclusive(Money inclusive, DateOnly date, VatTreatment treatment);
}

public sealed class FijiVatSchedule : IIndirectTaxSchedule
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

public sealed class NewZealandGstSchedule : IIndirectTaxSchedule
{
    public decimal StandardRateOn(DateOnly date)
    {
        if (date < new DateOnly(2010, 10, 1))
        {
            throw new DomainException(
                $"No reviewed New Zealand GST rate exists for {date:yyyy-MM-dd}.");
        }

        return 0.15m;
    }

    public VatResult CalculateFromExclusive(
        Money exclusive,
        DateOnly date,
        VatTreatment treatment)
    {
        var rate = treatment == VatTreatment.Standard ? StandardRateOn(date) : 0m;
        var tax = new Money(exclusive.Amount * rate, exclusive.Currency).Round();
        return new(
            exclusive.Round(),
            tax,
            new Money(exclusive.Amount + tax.Amount, exclusive.Currency).Round(),
            rate,
            treatment);
    }

    public VatResult CalculateFromInclusive(
        Money inclusive,
        DateOnly date,
        VatTreatment treatment)
    {
        var roundedInclusive = inclusive.Round();
        var rate = treatment == VatTreatment.Standard ? StandardRateOn(date) : 0m;
        var exclusive = new Money(
            roundedInclusive.Amount / (1m + rate),
            inclusive.Currency).Round();
        var tax = new Money(
            roundedInclusive.Amount - exclusive.Amount,
            inclusive.Currency).Round();
        return new(exclusive, tax, roundedInclusive, rate, treatment);
    }
}

public static class IndirectTaxSchedules
{
    public static IIndirectTaxSchedule For(string countryCode) =>
        countryCode.ToUpperInvariant() switch
        {
            "FJ" => new FijiVatSchedule(),
            "NZ" => new NewZealandGstSchedule(),
            _ => throw new DomainException(
                $"No reviewed indirect-tax schedule exists for {countryCode.ToUpperInvariant()}.")
        };
}

public sealed record VatResult(Money Exclusive, Money Vat, Money Inclusive, decimal Rate, VatTreatment Treatment);
