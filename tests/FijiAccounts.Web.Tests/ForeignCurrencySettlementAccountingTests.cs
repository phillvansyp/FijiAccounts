using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class ForeignCurrencySettlementAccountingTests
{
    [Fact]
    public async Task CustomerReceipt_PostsRealisedGainWithoutChangingInvoiceRate()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var invoice = await test.SalesInvoices.CreateAndPostAsync(
            test.UserId,
            new SalesInvoiceRequest(
                test.Organisation.Id,
                test.Customer.Id,
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 15),
                [new("USD invoice", 1m, 100m, VatTreatment.ZeroRated, test.Account("4000").Id)],
                Currency: "USD",
                ExchangeRateToBase: 2m));
        var service = new CustomerReceiptService(
            test.Db, test.Access, test.Posting, test.Reconciliation, test.Notifications);

        var receipt = await service.RecordAsync(
            test.UserId,
            new CustomerReceiptRequest(
                test.Organisation.Id,
                invoice.Id,
                new DateOnly(2026, 8, 20),
                "USD-RECEIPT",
                210m,
                test.Account("1000").Id,
                TransactionAmount: 100m));

        var paid = await test.Db.SalesInvoices.AsNoTracking().SingleAsync(x => x.Id == invoice.Id);
        Assert.Equal(2m, paid.ExchangeRateToBase);
        Assert.Equal(100m, paid.TransactionAmountPaid);
        Assert.Equal(200m, paid.AmountPaid);
        Assert.Equal(InvoiceStatus.Paid, paid.Status);
        Assert.Equal(2.1m, receipt.ExchangeRateToBase);
        Assert.Equal(10m, receipt.RealisedExchangeDifference);
        Assert.Equal(210m, await test.AccountBalanceAsync("1000"));
        Assert.Equal(0m, await test.AccountBalanceAsync("1100"));
        Assert.Equal(-10m, await test.AccountBalanceAsync("4300"));
    }

    [Fact]
    public async Task SupplierPayment_PostsRealisedLossWithoutChangingBillRate()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var bill = await test.Purchasing.PostBillAsync(
            test.UserId,
            new SupplierBillRequest(
                test.Organisation.Id,
                test.Supplier.Id,
                "USD-BILL",
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 15),
                [new("USD supplier bill", 1m, 100m, VatTreatment.ZeroRated, test.Account("6500").Id)],
                Currency: "USD",
                ExchangeRateToBase: 2m));

        var payment = await test.Purchasing.PayBillAsync(
            test.UserId,
            new SupplierPaymentRequest(
                test.Organisation.Id,
                bill.Id,
                new DateOnly(2026, 8, 20),
                "USD-PAYMENT",
                210m,
                test.Account("1000").Id,
                TransactionAmount: 100m));

        var paid = await test.Db.SupplierBills.AsNoTracking().SingleAsync(x => x.Id == bill.Id);
        Assert.Equal(2m, paid.ExchangeRateToBase);
        Assert.Equal(100m, paid.TransactionAmountPaid);
        Assert.Equal(200m, paid.AmountPaid);
        Assert.Equal(BillStatus.Paid, paid.Status);
        Assert.Equal(2.1m, payment.ExchangeRateToBase);
        Assert.Equal(10m, payment.RealisedExchangeDifference);
        Assert.Equal(-210m, await test.AccountBalanceAsync("1000"));
        Assert.Equal(0m, await test.AccountBalanceAsync("2000"));
        Assert.Equal(10m, await test.AccountBalanceAsync("6950"));
    }
}
