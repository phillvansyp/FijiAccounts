using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class CustomerReceiptAccountingTests
{
    [Fact]
    public async Task RecordAsync_WhenReceivablesControlAccountIsInactive_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice =
            await CreateInvoiceAsync(test);

        var receivables = test.Account("1100");
        receivables.IsActive = false;
        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var receiptCountBefore =
            await test.Db.CustomerReceipts.CountAsync();

        var service =
            new CustomerReceiptService(
                test.Db,
                test.Access,
                test.Posting,
                test.Reconciliation);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.RecordAsync(
                        test.UserId,
                        new CustomerReceiptRequest(
                            OrganisationId: test.Organisation.Id,
                            SalesInvoiceId: invoice.Id,
                            Date: new DateOnly(2026, 8, 20),
                            Reference: "REC-INACTIVE-AR",
                            Amount: 50m,
                            BankAccountId: test.Account("1000").Id)));

        Assert.Contains(
            "1100",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

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

        Assert.Equal(0m, afterRejected.AmountPaid);
        Assert.Equal(InvoiceStatus.Posted, afterRejected.Status);
    }

    [Fact]
    public async Task RecordAsync_WhenReceivablesControlAccountHasWrongType_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice =
            await CreateInvoiceAsync(test);

        var receivables = test.Account("1100");
        receivables.Type = AccountType.Liability;
        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var receiptCountBefore =
            await test.Db.CustomerReceipts.CountAsync();

        var service =
            new CustomerReceiptService(
                test.Db,
                test.Access,
                test.Posting,
                test.Reconciliation);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.RecordAsync(
                        test.UserId,
                        new CustomerReceiptRequest(
                            OrganisationId: test.Organisation.Id,
                            SalesInvoiceId: invoice.Id,
                            Date: new DateOnly(2026, 8, 20),
                            Reference: "REC-WRONG-AR-TYPE",
                            Amount: 50m,
                            BankAccountId: test.Account("1000").Id)));

        Assert.Contains(
            "1100",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

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

        Assert.Equal(0m, afterRejected.AmountPaid);
        Assert.Equal(InvoiceStatus.Posted, afterRejected.Status);
    }

        [Fact]
    public async Task RecordAsync_InsideCompletedReconciliation_IsRejectedWithoutMutation()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice =
            await CreateInvoiceAsync(test);

        var bank = test.Account("1000");

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

        var receiptCountBefore =
            await test.Db.CustomerReceipts.CountAsync();

        var allocationCountBefore =
            await test.Db.CustomerReceiptAllocations.CountAsync();

        var auditCountBefore =
            await test.Db.AuditEvents.CountAsync();

        var service =
            new CustomerReceiptService(
                test.Db,
                test.Access,
                test.Posting,
                test.Reconciliation);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.RecordAsync(
                        test.UserId,
                        new CustomerReceiptRequest(
                            OrganisationId: test.Organisation.Id,
                            SalesInvoiceId: invoice.Id,
                            Date: new DateOnly(2026, 8, 20),
                            Reference: "REC-LOCKED-001",
                            Amount: 50m,
                            BankAccountId: bank.Id)));

        Assert.Equal(
            "A journal cannot post to a bank account inside a completed reconciliation period.",
            ex.Message);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());

        Assert.Equal(
            receiptCountBefore,
            await test.Db.CustomerReceipts.CountAsync());

        Assert.Equal(
            allocationCountBefore,
            await test.Db.CustomerReceiptAllocations.CountAsync());

        Assert.Equal(
            auditCountBefore,
            await test.Db.AuditEvents.CountAsync());

        var afterRejected =
            await test.Db.SalesInvoices
                .AsNoTracking()
                .SingleAsync(x => x.Id == invoice.Id);

        Assert.Equal(0m, afterRejected.AmountPaid);
        Assert.Equal(InvoiceStatus.Posted, afterRejected.Status);
    }

    private static Task<SalesInvoice> CreateInvoiceAsync(
        AccountingTestDatabase test) =>
        test.SalesInvoices.CreateAndPostAsync(
            test.UserId,
            new SalesInvoiceRequest(
                OrganisationId: test.Organisation.Id,
                CustomerId: test.Customer.Id,
                IssueDate: new DateOnly(2026, 8, 18),
                DueDate: new DateOnly(2026, 9, 17),
                Lines:
                [
                    new SalesInvoiceLineRequest(
                        Description: "Customer receipt control test",
                        Quantity: 1m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        RevenueAccountId: test.Account("4000").Id)
                ]));
}