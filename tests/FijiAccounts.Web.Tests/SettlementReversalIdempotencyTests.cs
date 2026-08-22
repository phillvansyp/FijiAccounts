using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SettlementReversalIdempotencyTests
{
    [Fact]
    public async Task ReverseReceiptAsync_RejectsSecondReversalWithoutChangingAccounting()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice =
            await test.SalesInvoices.CreateAndPostAsync(
                test.UserId,
                new SalesInvoiceRequest(
                    OrganisationId: test.Organisation.Id,
                    CustomerId: test.Customer.Id,
                    IssueDate: new DateOnly(2026, 8, 18),
                    DueDate: new DateOnly(2026, 9, 17),
                    Lines:
                    [
                        new SalesInvoiceLineRequest(
                            Description: "Receipt reversal idempotency test",
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
                test.Reconciliation,
                test.Notifications);

        var receipt =
            await receipts.RecordAsync(
                test.UserId,
                new CustomerReceiptRequest(
                    OrganisationId: test.Organisation.Id,
                    SalesInvoiceId: invoice.Id,
                    Date: new DateOnly(2026, 8, 19),
                    Reference: "REC-IDEMP-001",
                    Amount: 50m,
                    BankAccountId: test.Account("1000").Id));

        await receipts.ReverseAsync(
            test.UserId,
            test.Organisation.Id,
            receipt.Id,
            new DateOnly(2026, 8, 20),
            "First reversal");

        var journalCountAfterFirst =
            await test.Db.PostedJournals.CountAsync();

        var reversalCountAfterFirst =
            await test.Db.CustomerReceiptReversals.CountAsync();

        var invoiceAfterFirst =
            await test.Db.SalesInvoices
                .AsNoTracking()
                .SingleAsync(x => x.Id == invoice.Id);

        Assert.Equal(0m, invoiceAfterFirst.AmountPaid);
        Assert.Equal(InvoiceStatus.Posted, invoiceAfterFirst.Status);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    receipts.ReverseAsync(
                        test.UserId,
                        test.Organisation.Id,
                        receipt.Id,
                        new DateOnly(2026, 8, 21),
                        "Second reversal"));

        Assert.Equal(
            "This receipt has already been reversed.",
            exception.Message);

        Assert.Equal(
            journalCountAfterFirst,
            await test.Db.PostedJournals.CountAsync());

        Assert.Equal(
            reversalCountAfterFirst,
            await test.Db.CustomerReceiptReversals.CountAsync());

        var invoiceAfterSecond =
            await test.Db.SalesInvoices
                .AsNoTracking()
                .SingleAsync(x => x.Id == invoice.Id);

        Assert.Equal(0m, invoiceAfterSecond.AmountPaid);
        Assert.Equal(InvoiceStatus.Posted, invoiceAfterSecond.Status);
    }

    [Fact]
    public async Task ReversePaymentAsync_RejectsSecondReversalWithoutChangingAccounting()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill =
            await test.Purchasing.PostBillAsync(
                test.UserId,
                new SupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "SUP-IDEMP-001",
                    BillDate: new DateOnly(2026, 8, 18),
                    DueDate: new DateOnly(2026, 9, 17),
                    Lines:
                    [
                        new SupplierBillLineRequest(
                            Description: "Payment reversal idempotency test",
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
                    Date: new DateOnly(2026, 8, 19),
                    Reference: "PAY-IDEMP-001",
                    Amount: 50m,
                    BankAccountId: test.Account("1000").Id));

        await test.Purchasing.ReversePaymentAsync(
            test.UserId,
            test.Organisation.Id,
            payment.Id,
            new DateOnly(2026, 8, 20),
            "First reversal");

        var journalCountAfterFirst =
            await test.Db.PostedJournals.CountAsync();

        var reversalCountAfterFirst =
            await test.Db.SupplierPaymentReversals.CountAsync();

        var billAfterFirst =
            await test.Db.SupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == bill.Id);

        Assert.Equal(0m, billAfterFirst.AmountPaid);
        Assert.Equal(BillStatus.Posted, billAfterFirst.Status);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    test.Purchasing.ReversePaymentAsync(
                        test.UserId,
                        test.Organisation.Id,
                        payment.Id,
                        new DateOnly(2026, 8, 21),
                        "Second reversal"));

        Assert.Equal(
            "This payment has already been reversed.",
            exception.Message);

        Assert.Equal(
            journalCountAfterFirst,
            await test.Db.PostedJournals.CountAsync());

        Assert.Equal(
            reversalCountAfterFirst,
            await test.Db.SupplierPaymentReversals.CountAsync());

        var billAfterSecond =
            await test.Db.SupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == bill.Id);

        Assert.Equal(0m, billAfterSecond.AmountPaid);
        Assert.Equal(BillStatus.Posted, billAfterSecond.Status);
    }

    [Fact]
public async Task ReverseReceiptAsync_WhenCreditRemains_ChangesInvoiceToPartPaid()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var invoice =
        await test.SalesInvoices.CreateAndPostAsync(
            test.UserId,
            new SalesInvoiceRequest(
                OrganisationId: test.Organisation.Id,
                CustomerId: test.Customer.Id,
                IssueDate: new DateOnly(2026, 8, 18),
                DueDate: new DateOnly(2026, 9, 17),
                Lines:
                [
                    new SalesInvoiceLineRequest(
                        Description: "Receipt reversal with credit test",
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
            test.Reconciliation,
            test.Notifications);

    var receipt =
        await receipts.RecordAsync(
            test.UserId,
            new CustomerReceiptRequest(
                OrganisationId: test.Organisation.Id,
                SalesInvoiceId: invoice.Id,
                Date: new DateOnly(2026, 8, 19),
                Reference: "REC-CREDIT-REV-001",
                Amount: 25m,
                BankAccountId: test.Account("1000").Id));

    var credits =
        new SalesCreditNoteService(
            test.Db,
            test.Access,
            test.Posting);

    await credits.CreateAsync(
        test.UserId,
        new SalesCreditNoteRequest(
            OrganisationId: test.Organisation.Id,
            SalesInvoiceId: invoice.Id,
            Date: new DateOnly(2026, 8, 20),
            Reason: "Credit remaining balance",
            Amount: invoice.Total - 25m,
            RestockTrackedItems: false));

    var settled =
        await test.Db.SalesInvoices
            .AsNoTracking()
            .SingleAsync(x => x.Id == invoice.Id);

    Assert.Equal(InvoiceStatus.Credited, settled.Status);

    await receipts.ReverseAsync(
        test.UserId,
        test.Organisation.Id,
        receipt.Id,
        new DateOnly(2026, 8, 21),
        "Reverse receipt while credit remains");

    var reloaded =
        await test.Db.SalesInvoices
            .AsNoTracking()
            .SingleAsync(x => x.Id == invoice.Id);

    Assert.Equal(0m, reloaded.AmountPaid);
    Assert.Equal(invoice.Total - 25m, reloaded.AmountCredited);
    Assert.Equal(InvoiceStatus.PartPaid, reloaded.Status);
}

    [Fact]
public async Task ReversePaymentAsync_WhenCreditRemains_ChangesBillToPartPaid()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var bill =
        await test.Purchasing.PostBillAsync(
            test.UserId,
            new SupplierBillRequest(
                OrganisationId: test.Organisation.Id,
                SupplierId: test.Supplier.Id,
                SupplierReference: "SUP-CREDIT-REV-001",
                BillDate: new DateOnly(2026, 8, 18),
                DueDate: new DateOnly(2026, 9, 17),
                Lines:
                [
                    new SupplierBillLineRequest(
                        Description: "Payment reversal with credit test",
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
                Date: new DateOnly(2026, 8, 19),
                Reference: "SUP-CREDIT-PAY-001",
                Amount: 25m,
                BankAccountId: test.Account("1000").Id));

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
            Date: new DateOnly(2026, 8, 20),
            Reason: "Credit remaining balance",
            Amount: bill.Total - 25m,
            ReturnTrackedItems: false));

    var settled =
        await test.Db.SupplierBills
            .AsNoTracking()
            .SingleAsync(x => x.Id == bill.Id);

    Assert.Equal(BillStatus.Credited, settled.Status);

    await test.Purchasing.ReversePaymentAsync(
        test.UserId,
        test.Organisation.Id,
        payment.Id,
        new DateOnly(2026, 8, 21),
        "Reverse payment while credit remains");

    var reloaded =
        await test.Db.SupplierBills
            .AsNoTracking()
            .SingleAsync(x => x.Id == bill.Id);

    Assert.Equal(0m, reloaded.AmountPaid);
    Assert.Equal(bill.Total - 25m, reloaded.AmountCredited);
    Assert.Equal(BillStatus.PartPaid, reloaded.Status);
}
}
