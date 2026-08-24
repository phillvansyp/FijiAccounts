using FijiAccounts.Domain.Tax;
using FijiAccounts.Domain.Accounting;
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

    [Fact]
public async Task PayBillAsync_WhenAccountsPayableControlAccountHasWrongType_IsRejected()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var bill =
        await test.Purchasing.PostBillAsync(
            test.UserId,
            new SupplierBillRequest(
                OrganisationId: test.Organisation.Id,
                SupplierId: test.Supplier.Id,
                SupplierReference: "SUP-PAY-WRONG-AP-TYPE",
                BillDate: new DateOnly(2026, 8, 18),
                DueDate: new DateOnly(2026, 9, 17),
                Lines:
                [
                    new SupplierBillLineRequest(
                        Description: "Supplier payment AP integrity test",
                        Quantity: 1m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        ExpenseAccountId: test.Account("6500").Id)
                ]));

    var payable = test.Account("2000");
    payable.Type = AccountType.Asset;
    await test.Db.SaveChangesAsync();

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
                        Reference: "PAY-WRONG-AP-TYPE",
                        Amount: 50m,
                        BankAccountId: test.Account("1000").Id)));

    Assert.Contains(
        "2000",
        ex.Message,
        StringComparison.OrdinalIgnoreCase);

    Assert.Equal(
        journalCountBefore,
        await test.Db.PostedJournals.CountAsync());

    Assert.Equal(
        paymentCountBefore,
        await test.Db.SupplierPayments.CountAsync());

    var afterRejected =
        await test.Db.SupplierBills
            .AsNoTracking()
            .SingleAsync(x => x.Id == bill.Id);

    Assert.Equal(0m, afterRejected.AmountPaid);
    Assert.Equal(BillStatus.Posted, afterRejected.Status);
}

        [Fact]
    public async Task PayBillAsync_InsideCompletedReconciliation_IsRejectedWithoutMutation()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank = test.Account("1000");

        var bill =
            await test.Purchasing.PostBillAsync(
                test.UserId,
                new SupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "SUP-PAY-LOCKED-001",
                    BillDate: new DateOnly(2026, 8, 18),
                    DueDate: new DateOnly(2026, 9, 17),
                    Lines:
                    [
                        new SupplierBillLineRequest(
                            Description: "Locked reconciliation payment test",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            ExpenseAccountId: test.Account("6500").Id)
                    ]));

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

        var paymentCountBefore =
            await test.Db.SupplierPayments.CountAsync();

        var auditCountBefore =
            await test.Db.AuditEvents.CountAsync();

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    test.Purchasing.PayBillAsync(
                        test.UserId,
                        new SupplierPaymentRequest(
                            OrganisationId: test.Organisation.Id,
                            SupplierBillId: bill.Id,
                            Date: new DateOnly(2026, 8, 20),
                            Reference: "PAY-LOCKED-001",
                            Amount: 50m,
                            BankAccountId: bank.Id)));

        Assert.Equal(
            "A journal cannot post to a bank account inside a completed reconciliation period.",
            ex.Message);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());

        Assert.Equal(
            paymentCountBefore,
            await test.Db.SupplierPayments.CountAsync());

        Assert.Equal(
            auditCountBefore,
            await test.Db.AuditEvents.CountAsync());

        var afterRejected =
            await test.Db.SupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == bill.Id);

        Assert.Equal(0m, afterRejected.AmountPaid);
        Assert.Equal(BillStatus.Posted, afterRejected.Status);
    }

    [Fact]
    public async Task PayBillAsync_WhenStatementMovementAlreadyCoded_IsRejected()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var bank = test.Account("1000");
        var date = new DateOnly(2026, 8, 18);
        var bill = await test.Purchasing.PostBillAsync(
            test.UserId,
            new SupplierBillRequest(
                test.Organisation.Id,
                test.Supplier.Id,
                "NO-DUPLICATE-PAYMENT",
                date,
                date.AddDays(30),
                [new("Office supplies", 1m, 100m, VatTreatment.OutOfScope, test.Account("6500").Id)]));
        var statement = await test.Reconciliation.AddStatementLineAsync(
            test.UserId,
            new StatementLineRequest(
                test.Organisation.Id,
                bank.Id,
                date,
                "Supplier card purchase",
                null,
                -100m));
        await test.BankCoding.PostAndReconcileAsync(
            test.UserId,
            new BankTransactionCodingRequest(
                test.Organisation.Id,
                statement.Id,
                "6500",
                statement.Description,
                VatTreatment.OutOfScope));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            test.Purchasing.PayBillAsync(
                test.UserId,
                new SupplierPaymentRequest(
                    test.Organisation.Id,
                    bill.Id,
                    date,
                    "PAY-LATER",
                    100m,
                    bank.Id)));

        Assert.Contains("already coded from the statement", exception.Message);
    }
}
