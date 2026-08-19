using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SupplierPaymentReconciliationIntegrityTests
{
    [Fact]
    public async Task ReversePaymentAsync_Throws_WhenPaymentIsInsideCompletedReconciliation()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank = test.Account("1000");

        bank.BankAccountKind =
            BankAccountKind.DebitCard;

        await test.Db.SaveChangesAsync();

        var bill =
            await test.Purchasing.PostBillAsync(
                test.UserId,
                new SupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "SUP-001",
                    BillDate: new DateOnly(2026, 8, 10),
                    DueDate: new DateOnly(2026, 9, 9),
                    Lines:
                    [
                        new SupplierBillLineRequest(
                            Description: "Consulting services",
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
                    Amount: 112.50m,
                    BankAccountId: bank.Id));

        test.Db.BankReconciliationSessions.Add(
            new BankReconciliationSession
            {
                OrganisationId = test.Organisation.Id,
                BankAccountId = bank.Id,
                StatementStartDate = new DateOnly(2026, 8, 1),
                StatementEndDate = new DateOnly(2026, 8, 31),
                IsCompleted = true,
                CreatedByUserId = test.UserId
            });

        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var reversalCountBefore =
            await test.Db.SupplierPaymentReversals.CountAsync();

        var billBefore =
            await test.Db.SupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == bill.Id);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    test.Purchasing.ReversePaymentAsync(
                        test.UserId,
                        test.Organisation.Id,
                        payment.Id,
                        new DateOnly(2026, 9, 1),
                        "Payment entered incorrectly"));

        Assert.Equal(
            "A supplier payment inside a completed bank reconciliation period cannot be reversed.",
            ex.Message);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());

        Assert.Equal(
            reversalCountBefore,
            await test.Db.SupplierPaymentReversals.CountAsync());

        var billAfter =
            await test.Db.SupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == bill.Id);

        Assert.Equal(
            billBefore.AmountPaid,
            billAfter.AmountPaid);

        Assert.Equal(
            billBefore.Status,
            billAfter.Status);
    }

    [Fact]
public async Task ReversePaymentAsync_AllowsPaymentOutsideCompletedReconciliation()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var bank = test.Account("1000");

    bank.BankAccountKind =
        BankAccountKind.DebitCard;

    await test.Db.SaveChangesAsync();

    var bill =
        await test.Purchasing.PostBillAsync(
            test.UserId,
            new SupplierBillRequest(
                OrganisationId: test.Organisation.Id,
                SupplierId: test.Supplier.Id,
                SupplierReference: "SUP-002",
                BillDate: new DateOnly(2026, 9, 1),
                DueDate: new DateOnly(2026, 9, 30),
                Lines:
                [
                    new SupplierBillLineRequest(
                        Description: "Consulting services",
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
                Date: new DateOnly(2026, 9, 5),
                Reference: "PAY-002",
                Amount: 112.50m,
                BankAccountId: bank.Id));

    test.Db.BankReconciliationSessions.Add(
        new BankReconciliationSession
        {
            OrganisationId = test.Organisation.Id,
            BankAccountId = bank.Id,
            StatementStartDate = new DateOnly(2026, 8, 1),
            StatementEndDate = new DateOnly(2026, 8, 31),
            IsCompleted = true,
            CreatedByUserId = test.UserId
        });

    await test.Db.SaveChangesAsync();

    var journalCountBefore =
        await test.Db.PostedJournals.CountAsync();

    var reversal =
        await test.Purchasing.ReversePaymentAsync(
            test.UserId,
            test.Organisation.Id,
            payment.Id,
            new DateOnly(2026, 9, 6),
            "Payment entered incorrectly");

    Assert.NotEqual(Guid.Empty, reversal.Id);

    Assert.Equal(
        journalCountBefore + 1,
        await test.Db.PostedJournals.CountAsync());

    Assert.Equal(
        1,
        await test.Db.SupplierPaymentReversals
            .CountAsync(x => x.SupplierPaymentId == payment.Id));

    var billAfter =
        await test.Db.SupplierBills
            .AsNoTracking()
            .SingleAsync(x => x.Id == bill.Id);

    Assert.Equal(0m, billAfter.AmountPaid);
    Assert.Equal(BillStatus.Posted, billAfter.Status);
}
}