using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class SupplierBillRegisterSummaryTests
{
    [Fact]
    public void Calculate_ExcludesVoidedBillsFromValueAndCount()
    {
        var bills = new[]
        {
            Bill(BillStatus.Posted, 23_367.62m),
            Bill(BillStatus.Paid, 11_280.00m),
            Bill(BillStatus.Voided, 2_744_610.00m),
            Bill(BillStatus.Voided, 2_526.72m)
        };

        var summary = SupplierBillRegisterSummary.Calculate(bills);

        Assert.Equal(34_647.62m, summary.Total);
        Assert.Equal(2, summary.Count);
    }

    private static SupplierBill Bill(BillStatus status, decimal total) =>
        new()
        {
            BillNumber = Guid.NewGuid().ToString("N"),
            SupplierReference = "test",
            Status = status,
            Total = total,
            CreatedByUserId = "test"
        };
}
