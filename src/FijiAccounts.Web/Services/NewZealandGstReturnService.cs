namespace FijiAccounts.Web.Services;

public sealed record NewZealandGstReturn(
    DateOnly From,
    DateOnly To,
    decimal Box5TotalSalesAndIncome,
    decimal Box6ZeroRatedSupplies,
    decimal Box7TaxableSales,
    decimal Box8GstOnSales,
    decimal Box9DebitAdjustments,
    decimal Box10TotalGstCollected,
    decimal Box11PurchasesAndExpenses,
    decimal Box12GstOnPurchases,
    decimal Box13CreditAdjustments,
    decimal Box14TotalGstCredits,
    decimal Box15NetGst)
{
    public bool IsRefund => Box15NetGst < 0m;
}

public sealed class NewZealandGstReturnService(VatWorkpaperService workpapers)
{
    public async Task<NewZealandGstReturn> GetInvoiceBasisAsync(
        Guid organisationId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var workpaper = await workpapers.GetAsync(
            organisationId,
            from,
            to,
            cancellationToken);
        var salesCreditsGross =
            workpaper.SalesCredits.Net + workpaper.SalesCredits.Tax;
        var box5 = Round(
            workpaper.Sales.StandardNet +
            workpaper.Sales.StandardTax +
            workpaper.Sales.ZeroRatedNet -
            salesCreditsGross);
        var box6 = Round(workpaper.Sales.ZeroRatedNet);
        var box7 = Round(box5 - box6);
        var box8 = GstFraction(box7);
        var box9 = 0m;
        var box10 = Round(box8 + box9);
        var purchaseCreditsGross =
            workpaper.SupplierCredits.Net + workpaper.SupplierCredits.Tax;
        var box11 = Round(
            workpaper.Purchases.StandardNet +
            workpaper.Purchases.StandardTax -
            purchaseCreditsGross);
        var box12 = GstFraction(box11);
        var box13 = 0m;
        var box14 = Round(box12 + box13);
        return new(
            from,
            to,
            box5,
            box6,
            box7,
            box8,
            box9,
            box10,
            box11,
            box12,
            box13,
            box14,
            Round(box10 - box14));
    }

    private static decimal GstFraction(decimal inclusiveAmount) =>
        Round(inclusiveAmount * 3m / 23m);

    private static decimal Round(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}
