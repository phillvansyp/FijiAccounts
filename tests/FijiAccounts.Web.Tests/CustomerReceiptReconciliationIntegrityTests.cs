using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class CustomerReceiptReconciliationIntegrityTests
{
    [Fact]
    public async Task ReverseAsync_Throws_WhenReceiptIsInsideCompletedReconciliation()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank = test.Account("1000");

        bank.BankAccountKind =
            BankAccountKind.DebitCard;

        await test.Db.SaveChangesAsync();

        var invoice =
            await test.SalesInvoices.CreateAndPostAsync(
                test.UserId,
                new SalesInvoiceRequest(
                    OrganisationId: test.Organisation.Id,
                    CustomerId: test.Customer.Id,
                    IssueDate: new DateOnly(2026, 8, 10),
                    DueDate: new DateOnly(2026, 9, 9),
                    Lines:
                    [
                        new SalesInvoiceLineRequest(
                            Description: "Consulting services",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id)
                    ]));

        var receipts =
            new CustomerReceiptService(
                test.Db,
                test.Access,
                test.Posting);

        var receipt =
            await receipts.RecordAsync(
                test.UserId,
                new CustomerReceiptRequest(
                    OrganisationId: test.Organisation.Id,
                    SalesInvoiceId: invoice.Id,
                    Date: new DateOnly(2026, 8, 18),
                    Reference: "RCPT-001",
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
            await test.Db.CustomerReceiptReversals.CountAsync();

        var invoiceBefore =
            await test.Db.SalesInvoices
                .AsNoTracking()
                .SingleAsync(x => x.Id == invoice.Id);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    receipts.ReverseAsync(
                        test.UserId,
                        test.Organisation.Id,
                        receipt.Id,
                        new DateOnly(2026, 9, 1),
                        "Receipt entered incorrectly"));

        Assert.Equal(
            "A customer receipt inside a completed bank reconciliation period cannot be reversed.",
            ex.Message);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());

        Assert.Equal(
            reversalCountBefore,
            await test.Db.CustomerReceiptReversals.CountAsync());

        var invoiceAfter =
            await test.Db.SalesInvoices
                .AsNoTracking()
                .SingleAsync(x => x.Id == invoice.Id);

        Assert.Equal(
            invoiceBefore.AmountPaid,
            invoiceAfter.AmountPaid);

        Assert.Equal(
            invoiceBefore.Status,
            invoiceAfter.Status);
    }

    [Fact]
public async Task ReverseAsync_AllowsReceiptOutsideCompletedReconciliation()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var bank = test.Account("1000");

    bank.BankAccountKind =
        BankAccountKind.DebitCard;

    await test.Db.SaveChangesAsync();

    var invoice =
        await test.SalesInvoices.CreateAndPostAsync(
            test.UserId,
            new SalesInvoiceRequest(
                OrganisationId: test.Organisation.Id,
                CustomerId: test.Customer.Id,
                IssueDate: new DateOnly(2026, 8, 10),
                DueDate: new DateOnly(2026, 9, 9),
                Lines:
                [
                    new SalesInvoiceLineRequest(
                        Description: "Consulting services",
                        Quantity: 1m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        RevenueAccountId: test.Account("4000").Id)
                ]));

    var receipts =
        new CustomerReceiptService(
            test.Db,
            test.Access,
            test.Posting);

    var receipt =
        await receipts.RecordAsync(
            test.UserId,
            new CustomerReceiptRequest(
                OrganisationId: test.Organisation.Id,
                SalesInvoiceId: invoice.Id,
                Date: new DateOnly(2026, 9, 5),
                Reference: "RCPT-002",
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
        await receipts.ReverseAsync(
            test.UserId,
            test.Organisation.Id,
            receipt.Id,
            new DateOnly(2026, 9, 6),
            "Receipt entered incorrectly");

    Assert.NotEqual(Guid.Empty, reversal.Id);

    Assert.Equal(
        journalCountBefore + 1,
        await test.Db.PostedJournals.CountAsync());

    Assert.Equal(
        1,
        await test.Db.CustomerReceiptReversals
            .CountAsync(x => x.CustomerReceiptId == receipt.Id));

    var invoiceAfter =
        await test.Db.SalesInvoices
            .AsNoTracking()
            .SingleAsync(x => x.Id == invoice.Id);

    Assert.Equal(0m, invoiceAfter.AmountPaid);
    Assert.Equal(InvoiceStatus.Posted, invoiceAfter.Status);
}
}