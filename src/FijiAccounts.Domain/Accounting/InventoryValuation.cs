namespace FijiAccounts.Domain.Accounting;

public static class InventoryValuation
{
    public static decimal WeightedAverage(decimal currentQuantity, decimal currentAverageCost, decimal receivedQuantity, decimal receivedUnitCost)
    {
        if (currentQuantity < 0 || currentAverageCost < 0 || receivedQuantity <= 0 || receivedUnitCost < 0)
            throw new DomainException("Inventory quantities and costs are invalid.");
        var quantity = currentQuantity + receivedQuantity;
        return quantity == 0 ? 0 : Math.Round(((currentQuantity * currentAverageCost) + (receivedQuantity * receivedUnitCost)) / quantity, 4);
    }

    public static decimal MovementValue(decimal quantity, decimal unitCost) => Math.Round(quantity * unitCost, 2);
}
