using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Services;

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
}