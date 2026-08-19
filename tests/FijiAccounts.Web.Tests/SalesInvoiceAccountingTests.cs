using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SalesInvoiceAccountingTests
{
    [Fact]
    public async Task PostInvoice_WithFijiVat_PostsBalancedArRevenueAndVatJournal()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var result =
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
                            Description: "Consulting services",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id)
                    ]));

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.NotNull(result.PostedJournalId);

        Assert.Equal(100m, result.Subtotal);
        Assert.Equal(12.50m, result.VatTotal);
        Assert.Equal(112.50m, result.Total);

        var journal =
            await test.LoadJournalAsync(
                result.PostedJournalId!.Value);

        var totalDebit =
            journal.Lines.Sum(x => x.Debit);

        var totalCredit =
            journal.Lines.Sum(x => x.Credit);

        Assert.Equal(totalDebit, totalCredit);

        Assert.Equal(112.50m, totalDebit);
        Assert.Equal(112.50m, totalCredit);

        var receivables =
            journal.Lines.Single(
                x => x.LedgerAccount.Code == "1100");

        var sales =
            journal.Lines.Single(
                x => x.LedgerAccount.Code == "4000");

        var vatPayable =
            journal.Lines.Single(
                x => x.LedgerAccount.Code == "2100");

        Assert.Equal(112.50m, receivables.Debit);
        Assert.Equal(0m, receivables.Credit);

        Assert.Equal(0m, sales.Debit);
        Assert.Equal(100m, sales.Credit);

        Assert.Equal(0m, vatPayable.Debit);
        Assert.Equal(12.50m, vatPayable.Credit);
    }

    [Fact]
    public async Task CreateAndPostAsync_WhenReceivablesControlAccountIsInactive_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var receivables = test.Account("1100");
        receivables.IsActive = false;

        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
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
                                    Description: "Inactive AR control test",
                                    Quantity: 1m,
                                    UnitPrice: 100m,
                                    VatTreatment: VatTreatment.Standard,
                                    RevenueAccountId: test.Account("4000").Id)
                            ])));

        Assert.Contains(
            "1100",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());
    }

    [Fact]
    public async Task CreateAndPostAsync_WhenReceivablesControlAccountHasWrongType_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var receivables = test.Account("1100");
        receivables.Type = AccountType.Liability;

        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
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
                                    Description: "Invalid AR control type test",
                                    Quantity: 1m,
                                    UnitPrice: 100m,
                                    VatTreatment: VatTreatment.Standard,
                                    RevenueAccountId: test.Account("4000").Id)
                            ])));

        Assert.Contains(
            "1100",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());
    }

    [Fact]
    public async Task CreateAndPostAsync_WhenVatPayableControlAccountIsInactive_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var vatPayable = test.Account("2100");
        vatPayable.IsActive = false;

        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
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
                                    Description: "Inactive VAT control test",
                                    Quantity: 1m,
                                    UnitPrice: 100m,
                                    VatTreatment: VatTreatment.Standard,
                                    RevenueAccountId: test.Account("4000").Id)
                            ])));

        Assert.Contains(
            "2100",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());
    }

    [Fact]
    public async Task CreateAndPostAsync_WhenVatPayableControlAccountHasWrongType_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var vatPayable = test.Account("2100");
        vatPayable.Type = AccountType.Asset;

        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
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
                                    Description: "Invalid VAT control type test",
                                    Quantity: 1m,
                                    UnitPrice: 100m,
                                    VatTreatment: VatTreatment.Standard,
                                    RevenueAccountId: test.Account("4000").Id)
                            ])));

        Assert.Contains(
            "2100",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());
    }
}