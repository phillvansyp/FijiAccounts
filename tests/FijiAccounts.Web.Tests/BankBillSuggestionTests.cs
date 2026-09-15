using System.Reflection;
using FijiAccounts.Web.Components.Pages;
using FijiAccounts.Web.Data;
namespace FijiAccounts.Web.Tests;

public sealed class BankBillSuggestionTests
{
    [Theory]
    [InlineData("92058716", "92058716", true)]
    [InlineData("92058716", "920587160", false)]
    [InlineData("92058716", "92057918", false)]
    public void FindsExactBillDespiteIncorrectFutureDate(string reference, string description, bool expected)
    {
        var bill = new SupplierBill { BillNumber = "BILL-001", SupplierReference = reference,
            CreatedByUserId = "user", BillDate = new DateOnly(2026, 9, 16), Total = 135.95m,
            VatTotal = 15.11m, Supplier = new BusinessParty { Name = "Rentokil" } };
        var page = new Banking();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(Banking).GetField("outstandingSupplierBills", flags)!.SetValue(page, new List<SupplierBill> { bill });
        var statement = new BankStatementLine { Description = description, Amount = -135.95m,
            TransactionDate = new DateOnly(2026, 1, 20) };
        var result = typeof(Banking).GetMethod("SuggestedSupplierBill", flags)!.Invoke(page, [statement]);
        if (expected) Assert.Same(bill, result); else Assert.Null(result);
    }

    [Fact]
    public async Task ExistingBillCannotOpenNewExpenseCoding()
    {
        var page = new Banking();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var bill = new SupplierBill { BillNumber = "BILL-001", SupplierReference = "92058716",
            CreatedByUserId = "user", BillDate = new DateOnly(2026, 9, 16), Total = 135.95m,
            VatTotal = 15.11m, Supplier = new BusinessParty { Name = "Rentokil" } };
        typeof(Banking).GetField("outstandingSupplierBills", flags)!.SetValue(page, new List<SupplierBill> { bill });
        var statement = new BankStatementLine { Description = "92058716", Amount = -135.95m,
            TransactionDate = new DateOnly(2026, 1, 20) };
        await (Task)typeof(Banking).GetMethod("BeginCoding", flags)!.Invoke(page, [statement])!;
        Assert.Null(typeof(Banking).GetField("codingStatementId", flags)!.GetValue(page));
        await (Task)typeof(Banking).GetMethod("PayAndReconcileSupplierBill", flags)!.Invoke(page, [statement, bill])!;
        Assert.Contains("correct the bill date", (string)typeof(Banking).GetField("status", flags)!.GetValue(page)!);
    }
}
