using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SettlementOutstandingBalanceTests
{
    [Fact]
    public async Task CustomerReceipts_CanReachExactBalance_ButCannotExceedIt()
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
                            Description: "Receipt outstanding balance test",
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

        await receipts.RecordAsync(
            test.UserId,
            new CustomerReceiptRequest(
                OrganisationId: test.Organisation.Id,
                SalesInvoiceId: invoice.Id,
                Date: new DateOnly(2026, 8, 19),
                Reference: "REC-BAL-001",
                Amount: 50m,
                BankAccountId: test.Account("1000").Id));

        var afterFirst =
            await test.Db.SalesInvoices
                .AsNoTracking()
                .SingleAsync(x => x.Id == invoice.Id);

        Assert.Equal(50m, afterFirst.AmountPaid);
        Assert.Equal(InvoiceStatus.PartPaid, afterFirst.Status);

        var remaining =
            invoice.Total - 50m;

        await receipts.RecordAsync(
            test.UserId,
            new CustomerReceiptRequest(
                OrganisationId: test.Organisation.Id,
                SalesInvoiceId: invoice.Id,
                Date: new DateOnly(2026, 8, 20),
                Reference: "REC-BAL-002",
                Amount: remaining,
                BankAccountId: test.Account("1000").Id));

        var fullyPaid =
            await test.Db.SalesInvoices
                .AsNoTracking()
                .SingleAsync(x => x.Id == invoice.Id);

        Assert.Equal(invoice.Total, fullyPaid.AmountPaid);
        Assert.Equal(InvoiceStatus.Paid, fullyPaid.Status);

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var receiptCountBefore =
            await test.Db.CustomerReceipts.CountAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                receipts.RecordAsync(
                    test.UserId,
                    new CustomerReceiptRequest(
                        OrganisationId: test.Organisation.Id,
                        SalesInvoiceId: invoice.Id,
                        Date: new DateOnly(2026, 8, 21),
                        Reference: "REC-BAL-003",
                        Amount: 0.01m,
                        BankAccountId: test.Account("1000").Id)));

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());

        Assert.Equal(
            receiptCountBefore,
            await test.Db.CustomerReceipts.CountAsync());

        var afterRejected =
            await test.Db.SalesInvoices
                .AsNoTracking()
                .SingleAsync(x => x.Id == invoice.Id);

        Assert.Equal(invoice.Total, afterRejected.AmountPaid);
        Assert.Equal(InvoiceStatus.Paid, afterRejected.Status);
    }

    [Fact]
    public async Task SupplierPayments_CanReachExactBalance_ButCannotExceedIt()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill =
            await test.Purchasing.PostBillAsync(
                test.UserId,
                new SupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "SUP-BAL-001",
                    BillDate: new DateOnly(2026, 8, 18),
                    DueDate: new DateOnly(2026, 9, 17),
                    Lines:
                    [
                        new SupplierBillLineRequest(
                            Description: "Supplier outstanding balance test",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            ExpenseAccountId: test.Account("6500").Id)
                    ]));

        await test.Purchasing.PayBillAsync(
            test.UserId,
            new SupplierPaymentRequest(
                OrganisationId: test.Organisation.Id,
                SupplierBillId: bill.Id,
                Date: new DateOnly(2026, 8, 19),
                Reference: "SUP-BAL-PAY-001",
                Amount: 50m,
                BankAccountId: test.Account("1000").Id));

        var afterFirst =
            await test.Db.SupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == bill.Id);

        Assert.Equal(50m, afterFirst.AmountPaid);
        Assert.Equal(BillStatus.PartPaid, afterFirst.Status);

        var remaining =
            bill.Total - 50m;

        await test.Purchasing.PayBillAsync(
            test.UserId,
            new SupplierPaymentRequest(
                OrganisationId: test.Organisation.Id,
                SupplierBillId: bill.Id,
                Date: new DateOnly(2026, 8, 20),
                Reference: "SUP-BAL-PAY-002",
                Amount: remaining,
                BankAccountId: test.Account("1000").Id));

        var fullyPaid =
            await test.Db.SupplierBills
                .AsNoTracking()
                .SingleAsync(x => x.Id == bill.Id);

        Assert.Equal(bill.Total, fullyPaid.AmountPaid);
        Assert.Equal(BillStatus.Paid, fullyPaid.Status);

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
                        Date: new DateOnly(2026, 8, 21),
                        Reference: "SUP-BAL-PAY-003",
                        Amount: 0.01m,
                        BankAccountId: test.Account("1000").Id)));

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

        Assert.Equal(bill.Total, afterRejected.AmountPaid);
        Assert.Equal(BillStatus.Paid, afterRejected.Status);
    }
}