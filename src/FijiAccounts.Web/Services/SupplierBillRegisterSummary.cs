using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record SupplierBillRegisterSummary(decimal Total, int Count)
{
    public static SupplierBillRegisterSummary Calculate(IEnumerable<SupplierBill> bills)
    {
        var includedBills = bills
            .Where(bill => bill.Status != BillStatus.Voided)
            .ToList();

        return new(includedBills.Sum(bill => bill.Total), includedBills.Count);
    }
}
