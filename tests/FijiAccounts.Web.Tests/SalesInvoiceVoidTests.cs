using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SalesInvoiceVoidTests
{
    [Fact]
    public async Task VoidInvoice_PostsExactReversingJournal()
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
                            Description: "Consulting services",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id)
                    ]));

        var originalJournalId =
            invoice.PostedJournalId!.Value;

        var originalJournal =
            await test.LoadJournalAsync(originalJournalId);

        await test.SalesInvoices.VoidAsync(
            test.UserId,
            test.Organisation.Id,
            invoice.Id,
            new DateOnly(2026, 8, 18));

        var reloadedInvoice =
            await test.Db.SalesInvoices
                .AsNoTracking()
                .SingleAsync(x => x.Id == invoice.Id);

        Assert.Equal(
            InvoiceStatus.Voided,
            reloadedInvoice.Status);

        var journals =
    await test.Db.PostedJournals
        .AsNoTracking()
        .Include(x => x.Lines)
        .Where(x =>
            x.OrganisationId == test.Organisation.Id)
        .ToListAsync();

journals = journals
    .OrderBy(x => x.PostedAt)
    .ToList();

        Assert.Equal(2, journals.Count);

        var reversingJournal =
            journals.Single(
                x => x.Id != originalJournalId);

        Assert.Equal(
            originalJournal.Lines.Sum(x => x.Debit),
            reversingJournal.Lines.Sum(x => x.Credit));

        Assert.Equal(
            originalJournal.Lines.Sum(x => x.Credit),
            reversingJournal.Lines.Sum(x => x.Debit));

        foreach (var originalLine in originalJournal.Lines)
        {
            var reversalLine =
                reversingJournal.Lines.Single(
                    x =>
                        x.LedgerAccountId ==
                        originalLine.LedgerAccountId);

            Assert.Equal(
                originalLine.Debit,
                reversalLine.Credit);

            Assert.Equal(
                originalLine.Credit,
                reversalLine.Debit);
        }

        Assert.Equal(
            0m,
            await test.AccountBalanceAsync("1100"));

        Assert.Equal(
            0m,
            await test.AccountBalanceAsync("4000"));

        Assert.Equal(
            0m,
            await test.AccountBalanceAsync("2100"));
    }
}