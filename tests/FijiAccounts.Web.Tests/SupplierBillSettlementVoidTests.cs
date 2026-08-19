using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SupplierBillSettlementVoidTests
{
    [Fact]
    public async Task VoidBillAsync_RejectsBillWithPaymentHistory_EvenIfStoredStateLooksPosted()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill =
            await CreateBillAsync(
                test,
                "SUP-VOID-SETTLEMENT-001");

        await test.Purchasing.PayBillAsync(
            test.UserId,
            new SupplierPaymentRequest(
                OrganisationId: test.Organisation.Id,
                SupplierBillId: bill.Id,
                Date: new DateOnly(2026, 8, 19),
                Reference: "SUP-VOID-PAY-001",
                Amount: 25m,
                BankAccountId: test.Account("1000").Id));

        var settledBill =
            await test.Db.SupplierBills
                .SingleAsync(x => x.Id == bill.Id);

        Assert.Equal(25m, settledBill.AmountPaid);
        Assert.Equal(BillStatus.PartPaid, settledBill.Status);

        // Simulate stale/corrupt denormalized state.
        // The SupplierPayment row still proves settlement history exists.
        settledBill.AmountPaid = 0m;
        settledBill.Status = BillStatus.Posted;

        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                test.Purchasing.VoidBillAsync(
                    test.UserId,
                    test.Organisation.Id,
                    bill.Id,
                    new DateOnly(2026, 8, 20),
                    "Should not void settled bill"));

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());

        var after =
            await test.Db.SupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == bill.Id);

        Assert.NotEqual(BillStatus.Voided, after.Status);

        Assert.Equal(
            1,
            await test.Db.SupplierPayments
                .CountAsync(x => x.SupplierBillId == bill.Id));
    }

    [Fact]
    public async Task VoidBillAsync_RejectsBillWithCreditHistory_EvenIfStoredStateLooksPosted()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill =
            await CreateBillAsync(
                test,
                "SUP-VOID-SETTLEMENT-002");

        var credits =
            new SupplierCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        await credits.CreateAsync(
            test.UserId,
            new SupplierCreditNoteRequest(
                OrganisationId: test.Organisation.Id,
                SupplierBillId: bill.Id,
                Date: new DateOnly(2026, 8, 19),
                Reason: "Supplier credit history",
                Amount: 25m,
                ReturnTrackedItems: false));

        var settledBill =
            await test.Db.SupplierBills
                .SingleAsync(x => x.Id == bill.Id);

        Assert.Equal(25m, settledBill.AmountCredited);
        Assert.Equal(BillStatus.PartPaid, settledBill.Status);

        // Simulate stale/corrupt denormalized state.
        // The SupplierCreditNote row still proves credit history exists.
        settledBill.AmountCredited = 0m;
        settledBill.Status = BillStatus.Posted;

        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                test.Purchasing.VoidBillAsync(
                    test.UserId,
                    test.Organisation.Id,
                    bill.Id,
                    new DateOnly(2026, 8, 20),
                    "Should not void credited bill"));

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());

        var after =
            await test.Db.SupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == bill.Id);

        Assert.NotEqual(BillStatus.Voided, after.Status);

        Assert.Equal(
            1,
            await test.Db.SupplierCreditNotes
                .CountAsync(x => x.SupplierBillId == bill.Id));
    }

    private static Task<SupplierBill> CreateBillAsync(
        AccountingTestDatabase test,
        string reference) =>
        test.Purchasing.PostBillAsync(
            test.UserId,
            new SupplierBillRequest(
                OrganisationId: test.Organisation.Id,
                SupplierId: test.Supplier.Id,
                SupplierReference: reference,
                BillDate: new DateOnly(2026, 8, 18),
                DueDate: new DateOnly(2026, 9, 17),
                Lines:
                [
                    new SupplierBillLineRequest(
                        Description: "Supplier settlement void integrity test",
                        Quantity: 1m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        ExpenseAccountId: test.Account("6500").Id)
                ]));
}