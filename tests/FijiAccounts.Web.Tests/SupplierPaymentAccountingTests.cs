using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SupplierPaymentAccountingTests
{
    [Fact]
    public async Task PayAndReverseSupplierBill_RestoresApAndBankBalances()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank =
            test.Account("1000");

        var bill =
            await test.Purchasing.PostBillAsync(
                test.UserId,
                new SupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "SUP-PAY-001",
                    BillDate: new DateOnly(2026, 8, 18),
                    DueDate: new DateOnly(2026, 9, 17),
                    Lines:
                    [
                        new SupplierBillLineRequest(
                            Description: "Office supplies",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            ExpenseAccountId: test.Account("6500").Id)
                    ]));

        var payment =
            await test.Purchasing.PayBillAsync(
                test.UserId,
                new SupplierPaymentRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierBillId: bill.Id,
                    Date: new DateOnly(2026, 8, 18),
                    Reference: "PAY-001",
                    Amount: bill.Total,
                    BankAccountId: bank.Id));

        var paidBill =
            await test.Db.SupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == bill.Id);

        Assert.Equal(BillStatus.Paid, paidBill.Status);
        Assert.Equal(bill.Total, paidBill.AmountPaid);

        Assert.Equal(
            0m,
            await test.AccountBalanceAsync("2000"));

        Assert.Equal(
            -bill.Total,
            await test.AccountBalanceAsync("1000"));

        var reversal =
            await test.Purchasing.ReversePaymentAsync(
                test.UserId,
                test.Organisation.Id,
                payment.Id,
                new DateOnly(2026, 8, 19),
                "Regression test reversal");

        Assert.NotEqual(Guid.Empty, reversal.Id);

        var reversedBill =
            await test.Db.SupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == bill.Id);

        Assert.Equal(BillStatus.Posted, reversedBill.Status);
        Assert.Equal(0m, reversedBill.AmountPaid);

        Assert.Equal(
            -bill.Total,
            await test.AccountBalanceAsync("2000"));

        Assert.Equal(
            0m,
            await test.AccountBalanceAsync("1000"));
    }
}