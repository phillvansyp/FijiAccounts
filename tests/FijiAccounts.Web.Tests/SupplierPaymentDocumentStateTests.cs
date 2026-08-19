using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SupplierPaymentDocumentStateTests
{
    [Fact]
    public async Task PayBillAsync_RejectsVoidedSupplierBill()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill =
            await test.Purchasing.PostBillAsync(
                test.UserId,
                new SupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "VOID-PAY-001",
                    BillDate: new DateOnly(2026, 8, 18),
                    DueDate: new DateOnly(2026, 9, 17),
                    Lines:
                    [
                        new SupplierBillLineRequest(
                            Description: "Voided bill payment test",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            ExpenseAccountId: test.Account("6500").Id)
                    ]));

        await test.Purchasing.VoidBillAsync(
            test.UserId,
            test.Organisation.Id,
            bill.Id,
            new DateOnly(2026, 8, 19),
            "Void before payment test");

        var voidedBill =
            await test.Db.SupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == bill.Id);

        Assert.Equal(
            BillStatus.Voided,
            voidedBill.Status);

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var paymentCountBefore =
            await test.Db.SupplierPayments.CountAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                test.Purchasing.PayBillAsync(
                    test.UserId,
                    new SupplierPaymentRequest(
                        OrganisationId: test.Organisation.Id,
                        SupplierBillId: bill.Id,
                        Date: new DateOnly(2026, 8, 20),
                        Reference: "PAY-VOID-001",
                        Amount: 25m,
                        BankAccountId: test.Account("1000").Id)));

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());

        Assert.Equal(
            paymentCountBefore,
            await test.Db.SupplierPayments.CountAsync());

        var after =
            await test.Db.SupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == bill.Id);

        Assert.Equal(
            BillStatus.Voided,
            after.Status);

        Assert.Equal(
            0m,
            after.AmountPaid);
    }

    [Fact]
public async Task PayBillAsync_RejectsFullyCreditedSupplierBill()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var bill =
        await test.Purchasing.PostBillAsync(
            test.UserId,
            new SupplierBillRequest(
                OrganisationId: test.Organisation.Id,
                SupplierId: test.Supplier.Id,
                SupplierReference: "CREDIT-PAY-001",
                BillDate: new DateOnly(2026, 8, 18),
                DueDate: new DateOnly(2026, 9, 17),
                Lines:
                [
                    new SupplierBillLineRequest(
                        Description: "Credited bill payment test",
                        Quantity: 1m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        ExpenseAccountId: test.Account("6500").Id)
                ]));

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
            Reason: "Full supplier credit before payment test",
            Amount: bill.Total,
            ReturnTrackedItems: false));

    var creditedBill =
        await test.Db.SupplierBills
            .AsNoTracking()
            .SingleAsync(x => x.Id == bill.Id);

    Assert.Equal(BillStatus.Credited, creditedBill.Status);

    var journalCountBefore =
        await test.Db.PostedJournals.CountAsync();

    var paymentCountBefore =
        await test.Db.SupplierPayments.CountAsync();

    var ex =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                test.Purchasing.PayBillAsync(
                    test.UserId,
                    new SupplierPaymentRequest(
                        OrganisationId: test.Organisation.Id,
                        SupplierBillId: bill.Id,
                        Date: new DateOnly(2026, 8, 20),
                        Reference: "PAY-CREDITED-001",
                        Amount: 1m,
                        BankAccountId: test.Account("1000").Id)));

    Assert.Equal(
        "Only outstanding posted supplier bills can be paid.",
        ex.Message);

    Assert.Equal(
        journalCountBefore,
        await test.Db.PostedJournals.CountAsync());

    Assert.Equal(
        paymentCountBefore,
        await test.Db.SupplierPayments.CountAsync());
}
}