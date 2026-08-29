using FijiAccounts.Domain.Fiscalisation;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

namespace FijiAccounts.Web.Tests;

public sealed class FiscalisationConfigurationServiceTests
{
    [Fact]
    public async Task EnableRequiresEveryVerifiedTaxLabel()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new FiscalisationConfigurationService(
            test.Db, test.Access, new TestEnvironment("Development"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateAsync(
                test.UserId,
                new UpdateFiscalisationConfigurationRequest(
                    test.Organisation.Id,
                    true,
                    FiscalPaymentType.Card,
                    "STANDARD",
                    null,
                    null,
                    null)));

        Assert.Contains("verified SDC label for every VAT treatment", error.Message);
        Assert.Empty(test.Db.FiscalisationConfigurations);
    }

    [Fact]
    public async Task DevelopmentConfigurationIsStoredAndAudited()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new FiscalisationConfigurationService(
            test.Db, test.Access, new TestEnvironment("Development"));
        var request = new UpdateFiscalisationConfigurationRequest(
            test.Organisation.Id,
            true,
            FiscalPaymentType.MobileMoney,
            " STANDARD ",
            "ZERO",
            "EXEMPT",
            "OUT");

        var saved = await service.UpdateAsync(test.UserId, request);
        var loaded = await service.GetAsync(test.UserId, test.Organisation.Id);
        var labels = FiscalisationConfigurationService.TaxLabels(saved);

        Assert.True(saved.IsEnabled);
        Assert.Equal("STANDARD", loaded!.StandardTaxLabel);
        Assert.Equal(FiscalPaymentType.MobileMoney, loaded.DefaultPaymentType);
        Assert.Equal("ZERO", Assert.Single(labels[VatTreatment.ZeroRated]));
        Assert.True(await test.Db.AuditEvents.AnyAsync(
            x => x.EventType == "FiscalisationConfigurationUpdated"));
    }

    [Fact]
    public async Task ProductionCannotEnableWithoutAnAccreditedAdapter()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new FiscalisationConfigurationService(
            test.Db, test.Access, new TestEnvironment("Production"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateAsync(
                test.UserId,
                new UpdateFiscalisationConfigurationRequest(
                    test.Organisation.Id,
                    true,
                    FiscalPaymentType.Other,
                    "STANDARD",
                    "ZERO",
                    "EXEMPT",
                    "OUT")));

        Assert.Contains("accredited SDC adapter", error.Message);
    }

    private sealed class TestEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "FijiAccounts.Web.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = environmentName;
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
