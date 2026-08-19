using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SalesCreditNoteAccountingTests
{
    [Fact]
    public async Task CreateAsync_WhenReceivablesControlAccountIsInactive_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice = await CreateInvoiceAsync(test);

        var receivables = test.Account("1100");
        receivables.IsActive = false;
        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var service =
            new SalesCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new SalesCreditNoteRequest(
                            OrganisationId: test.Organisation.Id,
                            SalesInvoiceId: invoice.Id,
                            Date: new DateOnly(2026, 8, 20),
                            Reason: "Inactive receivables control test",
                            Amount: 50m,
                            RestockTrackedItems: false)));

        Assert.Contains(
            "1100",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_WhenReceivablesControlAccountHasWrongType_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice = await CreateInvoiceAsync(test);

        var receivables = test.Account("1100");
        receivables.Type = AccountType.Liability;
        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var service =
            new SalesCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new SalesCreditNoteRequest(
                            OrganisationId: test.Organisation.Id,
                            SalesInvoiceId: invoice.Id,
                            Date: new DateOnly(2026, 8, 20),
                            Reason: "Invalid receivables control type test",
                            Amount: 50m,
                            RestockTrackedItems: false)));

        Assert.Contains(
            "1100",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_WhenVatPayableControlAccountIsInactive_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice = await CreateInvoiceAsync(test);

        var vatPayable = test.Account("2100");
        vatPayable.IsActive = false;
        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var service =
            new SalesCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new SalesCreditNoteRequest(
                            OrganisationId: test.Organisation.Id,
                            SalesInvoiceId: invoice.Id,
                            Date: new DateOnly(2026, 8, 20),
                            Reason: "Inactive VAT payable control test",
                            Amount: 50m,
                            RestockTrackedItems: false)));

        Assert.Contains(
            "2100",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_WhenVatPayableControlAccountHasWrongType_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice = await CreateInvoiceAsync(test);

        var vatPayable = test.Account("2100");
        vatPayable.Type = AccountType.Asset;
        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var service =
            new SalesCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new SalesCreditNoteRequest(
                            OrganisationId: test.Organisation.Id,
                            SalesInvoiceId: invoice.Id,
                            Date: new DateOnly(2026, 8, 20),
                            Reason: "Invalid VAT payable control type test",
                            Amount: 50m,
                            RestockTrackedItems: false)));

        Assert.Contains(
            "2100",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());
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
                        Description: "Sales credit control test",
                        Quantity: 1m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        RevenueAccountId: test.Account("4000").Id)
                ]));
}