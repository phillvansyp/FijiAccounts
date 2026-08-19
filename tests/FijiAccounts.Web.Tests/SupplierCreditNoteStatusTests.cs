using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SupplierCreditNoteStatusTests
{
    [Fact]
    public async Task PartialCredit_ChangesPostedBillToPartPaid()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill = await CreateBillAsync(test, "SUP-CREDIT-001");

        var service =
            new SupplierCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        await service.CreateAsync(
            test.UserId,
            new SupplierCreditNoteRequest(
                OrganisationId: test.Organisation.Id,
                SupplierBillId: bill.Id,
                Date: new DateOnly(2026, 8, 20),
                Reason: "Partial supplier credit",
                Amount: 56.25m,
                ReturnTrackedItems: false));

        var reloaded =
            await test.Db.SupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == bill.Id);

        Assert.Equal(56.25m, reloaded.AmountCredited);
        Assert.Equal(BillStatus.PartPaid, reloaded.Status);
    }

    [Fact]
    public async Task FullCredit_ChangesBillToCredited()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill = await CreateBillAsync(test, "SUP-CREDIT-002");

        var service =
            new SupplierCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        await service.CreateAsync(
            test.UserId,
            new SupplierCreditNoteRequest(
                OrganisationId: test.Organisation.Id,
                SupplierBillId: bill.Id,
                Date: new DateOnly(2026, 8, 20),
                Reason: "Full supplier credit",
                Amount: bill.Total,
                ReturnTrackedItems: false));

        var reloaded =
            await test.Db.SupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == bill.Id);

        Assert.Equal(bill.Total, reloaded.AmountCredited);
        Assert.Equal(BillStatus.Credited, reloaded.Status);
    }

    [Fact]
    public async Task PaymentPlusCredit_ThatFullySettlesBill_ChangesBillToCredited()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill = await CreateBillAsync(test, "SUP-CREDIT-003");

        await test.Purchasing.PayBillAsync(
            test.UserId,
            new SupplierPaymentRequest(
                OrganisationId: test.Organisation.Id,
                SupplierBillId: bill.Id,
                Date: new DateOnly(2026, 8, 19),
                Reference: "SUP-PAY-CREDIT-001",
                Amount: 25m,
                BankAccountId: test.Account("1000").Id));

        var service =
            new SupplierCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        await service.CreateAsync(
            test.UserId,
            new SupplierCreditNoteRequest(
                OrganisationId: test.Organisation.Id,
                SupplierBillId: bill.Id,
                Date: new DateOnly(2026, 8, 20),
                Reason: "Credit remaining balance",
                Amount: bill.Total - 25m,
                ReturnTrackedItems: false));

        var reloaded =
            await test.Db.SupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == bill.Id);

        Assert.Equal(25m, reloaded.AmountPaid);
        Assert.Equal(bill.Total - 25m, reloaded.AmountCredited);
        Assert.Equal(BillStatus.Credited, reloaded.Status);
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
                        Description: "Supplier credit status test",
                        Quantity: 1m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        ExpenseAccountId: test.Account("6500").Id)
                ]));
}