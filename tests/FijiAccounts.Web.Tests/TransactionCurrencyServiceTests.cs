using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class TransactionCurrencyServiceTests
{
    [Fact]
    public void ParseRbfRates_ReadsPublicationDateAndFjdQuotes()
    {
        const string html = """
            <h2><a><strong>Exchange Rates</strong></a></h2>
            <p style="text-align: center;"><span>27 August 2026</span></p>
            <div><h4>USD</h4><div class="desc">0.4515</div></div>
            <div><h4>AUD</h4><div class="desc">0.6294</div></div>
            <div><h4>NZD</h4><div class="desc">0.7596</div></div>
            """;

        var publication = TransactionCurrencyService.ParseRbfRates(html);

        Assert.Equal(new DateOnly(2026, 8, 27), publication.EffectiveDate);
        Assert.Equal(0.4515m, publication.Rates["USD"]);
        Assert.Equal(0.6294m, publication.Rates["AUD"]);
        Assert.Equal(0.7596m, publication.Rates["NZD"]);
    }

    [Fact]
    public async Task ListAsync_IncludesStandardCurrenciesAndBaseCurrency()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new TransactionCurrencyService(test.Db, test.Access);

        var currencies = await service.ListAsync(test.UserId, test.Organisation.Id);

        Assert.Contains(currencies, x => x.Code == "FJD" && x.IsBaseCurrency && x.IsActive);
        Assert.Contains(currencies, x => x.Code == "USD" && x.IsActive);
        Assert.Contains(currencies, x => x.Code == "AUD" && x.IsActive);
        Assert.Contains(currencies, x => x.Code == "NZD" && x.IsActive);
    }

    [Fact]
    public async Task FindRateAsync_UsesLatestRateOnOrBeforeTransactionDate()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new TransactionCurrencyService(test.Db, test.Access);

        await service.SaveRateAsync(test.UserId, test.Organisation.Id, "USD", new DateOnly(2026, 8, 1), 2.20m);
        await service.SaveRateAsync(test.UserId, test.Organisation.Id, "USD", new DateOnly(2026, 8, 20), 2.25m);

        Assert.Equal(2.20m, await service.FindRateAsync(test.UserId, test.Organisation.Id, "USD", new DateOnly(2026, 8, 19)));
        Assert.Equal(2.25m, await service.FindRateAsync(test.UserId, test.Organisation.Id, "USD", new DateOnly(2026, 8, 27)));
        Assert.Null(await service.FindRateAsync(test.UserId, test.Organisation.Id, "USD", new DateOnly(2026, 7, 31)));
    }
}
