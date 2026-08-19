using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SettlementReversalPeriodLockTests
{
    [Fact]
    public async Task CustomerReceiptReversal_InsideLockedPeriod_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice =
            await test.SalesInvoices.CreateAndPostAsync(
                test.UserId,
                new SalesInvoiceRequest(
                    OrganisationId: test.Organisation.Id,
                    CustomerId: test.Customer.Id,
                    IssueDate: new DateOnly(2026, 7, 20),
                    DueDate: new DateOnly(2026, 8, 20),
                    Lines:
                    [
                        new SalesInvoiceLineRequest(
                            Description: "Receipt reversal lock test",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId:
                                test.Account("4000").Id)
                    ]));

                    var receipts =
    new CustomerReceiptService(
        test.Db,
        test.Access,
        test.Posting,
        test.Reconciliation);

        var receipt =
    await receipts.RecordAsync(
                test.UserId,
                new CustomerReceiptRequest(
                    OrganisationId: test.Organisation.Id,
                    SalesInvoiceId: invoice.Id,
                    Date: new DateOnly(2026, 7, 25),
                    Reference: "LOCK-REC-001",
                    Amount: 50m,
                    BankAccountId:
                        test.Account("1000").Id));

        await LockAugust2026Async(test);

        var before =
            await test.Db.PostedJournals.CountAsync();

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    receipts.ReverseAsync(
                        test.UserId,
                        test.Organisation.Id,
                        receipt.Id,
                        new DateOnly(2026, 8, 20),
                        "Locked period reversal"));

        Assert.Equal(
            "The accounting period is locked.",
            exception.Message);

        Assert.Equal(
            before,
            await test.Db.PostedJournals.CountAsync());

        Assert.False(
            await test.Db.CustomerReceiptReversals
                .AnyAsync(
                    x =>
                        x.CustomerReceiptId ==
                        receipt.Id));

        var reloadedInvoice =
            await test.Db.SalesInvoices
                .AsNoTracking()
                .SingleAsync(
                    x => x.Id == invoice.Id);

        Assert.Equal(
            50m,
            reloadedInvoice.AmountPaid);

        Assert.Equal(
            InvoiceStatus.PartPaid,
            reloadedInvoice.Status);
    }

    [Fact]
    public async Task SupplierPaymentReversal_InsideLockedPeriod_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill =
            await test.Purchasing.PostBillAsync(
                test.UserId,
                new SupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "LOCK-PAY-001",
                    BillDate: new DateOnly(2026, 7, 20),
                    DueDate: new DateOnly(2026, 8, 20),
                    Lines:
                    [
                        new SupplierBillLineRequest(
                            Description:
                                "Payment reversal lock test",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment:
                                VatTreatment.Standard,
                            ExpenseAccountId:
                                test.Account("6500").Id)
                    ]));

        var payment =
            await test.Purchasing.PayBillAsync(
                test.UserId,
                new SupplierPaymentRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierBillId: bill.Id,
                    Date: new DateOnly(2026, 7, 25),
                    Reference: "LOCK-PAYMENT-001",
                    Amount: 50m,
                    BankAccountId:
                        test.Account("1000").Id));

        await LockAugust2026Async(test);

        var before =
            await test.Db.PostedJournals.CountAsync();

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    test.Purchasing.ReversePaymentAsync(
                        test.UserId,
                        test.Organisation.Id,
                        payment.Id,
                        new DateOnly(2026, 8, 20),
                        "Locked period reversal"));

        Assert.Equal(
            "The accounting period is locked.",
            exception.Message);

        Assert.Equal(
            before,
            await test.Db.PostedJournals.CountAsync());

        Assert.False(
            await test.Db.SupplierPaymentReversals
                .AnyAsync(
                    x =>
                        x.SupplierPaymentId ==
                        payment.Id));

        var reloadedBill =
            await test.Db.SupplierBills
                .AsNoTracking()
                .SingleAsync(
                    x => x.Id == bill.Id);

        Assert.Equal(
            50m,
            reloadedBill.AmountPaid);

        Assert.Equal(
            BillStatus.PartPaid,
            reloadedBill.Status);
    }

    private static async Task LockAugust2026Async(
        AccountingTestDatabase test)
    {
        test.Db.AccountingPeriods.Add(
            new AccountingPeriod
            {
                OrganisationId =
                    test.Organisation.Id,
                Name =
                    "August 2026",
                StartsOn =
                    new DateOnly(2026, 8, 1),
                EndsOn =
                    new DateOnly(2026, 8, 31),
                IsLocked =
                    true,
                LockedAt =
                    DateTimeOffset.UtcNow,
                LockedByUserId =
                    test.UserId
            });

        await test.Db.SaveChangesAsync();
    }
}