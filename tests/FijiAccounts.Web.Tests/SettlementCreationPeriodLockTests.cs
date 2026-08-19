using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SettlementCreationPeriodLockTests
{
    [Fact]
    public async Task CustomerReceipt_InLockedPeriod_IsRejectedAndInvoiceRemainsUnpaid()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice =
            await test.SalesInvoices.CreateAndPostAsync(
                test.UserId,
                new SalesInvoiceRequest(
                    OrganisationId: test.Organisation.Id,
                    CustomerId: test.Customer.Id,
                    IssueDate: new DateOnly(2026, 7, 31),
                    DueDate: new DateOnly(2026, 8, 30),
                    Lines:
                    [
                        new SalesInvoiceLineRequest(
                            Description: "Receipt period lock test",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id)
                    ]));

        var receipts =
            new CustomerReceiptService(
                test.Db,
                test.Access,
                test.Posting,
                test.Reconciliation);

        await LockAugust2026Async(test);

        var journalCount =
            await test.Db.PostedJournals.CountAsync();

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    receipts.RecordAsync(
                        test.UserId,
                        new CustomerReceiptRequest(
                            OrganisationId: test.Organisation.Id,
                            SalesInvoiceId: invoice.Id,
                            Date: new DateOnly(2026, 8, 18),
                            Reference: "LOCK-RECEIPT-001",
                            Amount: invoice.Total,
                            BankAccountId: test.Account("1000").Id)));

        Assert.Equal(
            "The accounting period is locked.",
            ex.Message);

        Assert.Equal(
            journalCount,
            await test.Db.PostedJournals.CountAsync());

        var reloaded =
            await test.Db.SalesInvoices
                .AsNoTracking()
                .SingleAsync(x => x.Id == invoice.Id);

        Assert.Equal(0m, reloaded.AmountPaid);
        Assert.Equal(InvoiceStatus.Posted, reloaded.Status);
    }

    [Fact]
    public async Task SupplierPayment_InLockedPeriod_IsRejectedAndBillRemainsUnpaid()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill =
            await test.Purchasing.PostBillAsync(
                test.UserId,
                new SupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "LOCK-PAYMENT-BILL-001",
                    BillDate: new DateOnly(2026, 7, 31),
                    DueDate: new DateOnly(2026, 8, 30),
                    Lines:
                    [
                        new SupplierBillLineRequest(
                            Description: "Payment period lock test",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            ExpenseAccountId: test.Account("6500").Id)
                    ]));

        await LockAugust2026Async(test);

        var journalCount =
            await test.Db.PostedJournals.CountAsync();

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    test.Purchasing.PayBillAsync(
                        test.UserId,
                        new SupplierPaymentRequest(
                            OrganisationId: test.Organisation.Id,
                            SupplierBillId: bill.Id,
                            Date: new DateOnly(2026, 8, 18),
                            Reference: "LOCK-PAYMENT-001",
                            Amount: bill.Total,
                            BankAccountId: test.Account("1000").Id)));

        Assert.Equal(
            "The accounting period is locked.",
            ex.Message);

        Assert.Equal(
            journalCount,
            await test.Db.PostedJournals.CountAsync());

        var reloaded =
            await test.Db.SupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == bill.Id);

        Assert.Equal(0m, reloaded.AmountPaid);
        Assert.Equal(BillStatus.Posted, reloaded.Status);
    }

    private static async Task LockAugust2026Async(
        AccountingTestDatabase test)
    {
        test.Db.AccountingPeriods.Add(
            new AccountingPeriod
            {
                OrganisationId = test.Organisation.Id,
                Name = "August 2026",
                StartsOn = new DateOnly(2026, 8, 1),
                EndsOn = new DateOnly(2026, 8, 31),
                IsLocked = true,
                LockedAt = DateTimeOffset.UtcNow,
                LockedByUserId = test.UserId
            });

        await test.Db.SaveChangesAsync();
    }
}