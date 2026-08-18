using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;

namespace FijiAccounts.Domain.Tests;

public class AccountingTests
{
    [Fact]
    public void Balanced_journal_is_accepted()
    {
        var entry = new JournalEntry(Guid.NewGuid(), new DateOnly(2026, 8, 16), "INV-001",
        [
            new("1100", "Accounts receivable", 112.50m, 0m),
            new("4000", "Sales", 0m, 100m),
            new("2200", "VAT payable", 0m, 12.50m)
        ]);

        Assert.Equal(112.50m, entry.Lines.Sum(x => x.Debit));
    }

    [Fact]
    public void Unbalanced_journal_is_rejected() => Assert.Throws<DomainException>(() =>
        new JournalEntry(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), "BAD",
        [new("1100", "Debit", 100m, 0m), new("4000", "Credit", 0m, 99m)]));

    [Theory]
    [InlineData(2025, 7, 31, 15.00)]
    [InlineData(2025, 8, 1, 12.50)]
    public void Vat_schedule_uses_transaction_date(int year, int month, int day, decimal expectedVat)
    {
        var result = new FijiVatSchedule().CalculateFromExclusive(
            new Money(100m), new DateOnly(year, month, day), VatTreatment.Standard);

        Assert.Equal(expectedVat, result.Vat.Amount);
    }

    [Fact]
    public void Vat_schedule_extracts_vat_from_inclusive_purchase_amount()
    {
        var result = new FijiVatSchedule().CalculateFromInclusive(
            new Money(860.63m), new DateOnly(2026, 7, 15), VatTreatment.Standard);

        Assert.Equal(765.00m, result.Exclusive.Amount);
        Assert.Equal(95.63m, result.Vat.Amount);
        Assert.Equal(860.63m, result.Inclusive.Amount);
    }

    [Fact]
    public void Out_of_scope_inclusive_amount_has_no_vat()
    {
        var result = new FijiVatSchedule().CalculateFromInclusive(
            new Money(860.63m), new DateOnly(2026, 7, 15), VatTreatment.OutOfScope);

        Assert.Equal(860.63m, result.Exclusive.Amount);
        Assert.Equal(0m, result.Vat.Amount);
    }

    [Fact]
    public void Inventory_weighted_average_preserves_total_value()
    {
        var average = InventoryValuation.WeightedAverage(10m, 5m, 5m, 8m);
        Assert.Equal(6m, average);
        Assert.Equal(90m, InventoryValuation.MovementValue(15m, average));
    }

    [Theory]
    [InlineData(-1, 5, 1, 5)]
    [InlineData(1, 5, 0, 5)]
    public void Inventory_weighted_average_rejects_invalid_inputs(decimal currentQuantity, decimal currentCost, decimal receivedQuantity, decimal receivedCost) =>
        Assert.Throws<DomainException>(() => InventoryValuation.WeightedAverage(currentQuantity, currentCost, receivedQuantity, receivedCost));
}
