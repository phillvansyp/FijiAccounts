using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class OrganisationSettingsServiceTests
{
    [Fact]
    public async Task Owner_CanUpdateBusinessDetailsAndJurisdiction()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new OrganisationSettingsService(test.Db);

        var updated =
            await service.UpdateAsync(
                test.UserId,
                Request(test.Organisation.Id));
        var relocated =
            await service.ChangeJurisdictionAsync(
                test.UserId,
                test.Organisation.Id,
                "WS");

        Assert.Equal("Updated Company Limited", updated.LegalName);
        Assert.Equal("Updated Company", updated.TradingName);
        Assert.Equal("TIN-UPDATED", updated.Tin);
        Assert.Equal(14, updated.DefaultSalesInvoiceDueDays);
        Assert.Equal(20, updated.DefaultSupplierBillDueDays);
        Assert.Equal("WS", relocated.CountryCode);
        Assert.Equal("WST", relocated.BaseCurrency);
        Assert.Equal("Pacific/Apia", relocated.TimeZoneId);
        Assert.Equal("VAGST", relocated.TaxLabel);
    }

    [Fact]
    public async Task NonManager_CannotChangeOrganisationSettings()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new OrganisationSettingsService(test.Db);
        var otherUserId = Guid.NewGuid().ToString();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.UpdateAsync(
                otherUserId,
                Request(test.Organisation.Id)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ChangeJurisdictionAsync(
                otherUserId,
                test.Organisation.Id,
                "WS"));

        Assert.Equal(
            "Accounting Test Limited",
            await test.Db.Organisations
                .Where(x => x.Id == test.Organisation.Id)
                .Select(x => x.LegalName)
                .SingleAsync());
    }

    [Fact]
    public async Task Jurisdiction_CannotChangeAfterJournalPosting()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new OrganisationSettingsService(test.Db);

        test.Db.PostedJournals.Add(
            new PostedJournal
            {
                OrganisationId = test.Organisation.Id,
                SequenceNumber = 1,
                EntryDate = new DateOnly(2026, 8, 23),
                Reference = "SETTINGS-LOCK",
                PostedAt = DateTimeOffset.UtcNow,
                PostedByUserId = test.UserId
            });
        await test.Db.SaveChangesAsync();

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ChangeJurisdictionAsync(
                    test.UserId,
                    test.Organisation.Id,
                    "WS"));

        Assert.Contains(
            "after accounting transactions",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private static UpdateOrganisationSettingsRequest Request(
        Guid organisationId) =>
        new(
            organisationId,
            " Updated Company Limited ",
            " Updated Company ",
            " TIN-UPDATED ",
            PaymentTermType.DaysAfterDocumentDate,
            14,
            PaymentTermType.DayOfFollowingMonth,
            20);
}
