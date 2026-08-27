using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class SalesInvoicePostingValidationTests
{
    [Fact]
    public async Task CreateAndPostAsync_RejectsBlankLineDescription()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            test.SalesInvoices.CreateAndPostAsync(
                test.UserId,
                Request(test, " ", 100m)));

        Assert.Equal("Each invoice line needs a description before posting.", exception.Message);
    }

    [Fact]
    public async Task CreateAndPostAsync_RejectsZeroValueInvoice()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            test.SalesInvoices.CreateAndPostAsync(
                test.UserId,
                Request(test, "Consulting", 0m)));

        Assert.Equal("The invoice total must be greater than zero before posting.", exception.Message);
    }

    private static SalesInvoiceRequest Request(
        AccountingTestDatabase test,
        string description,
        decimal unitPrice) =>
        new(
            test.Organisation.Id,
            test.Customer.Id,
            new DateOnly(2026, 8, 28),
            new DateOnly(2026, 9, 4),
            [new(description, 1m, unitPrice, VatTreatment.ZeroRated, test.Account("4000").Id)],
            Currency: "NZD",
            ExchangeRateToBase: 1.31648236m);
}
