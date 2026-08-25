using System.Text.Json;
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
        Assert.Equal("Level 3, Test House, Suva", updated.BusinessAddress);
        Assert.True(updated.IsVatRegistered);
        Assert.Equal(new DateOnly(2020, 1, 1), updated.VatRegistrationDate);
        Assert.Equal(new DateOnly(2026, 1, 1), updated.ConversionDate);
        Assert.Equal(14, updated.DefaultSalesInvoiceDueDays);
        Assert.Equal(20, updated.DefaultSupplierBillDueDays);
        Assert.Equal("WS", relocated.CountryCode);
        Assert.Equal("WST", relocated.BaseCurrency);
        Assert.Equal("Pacific/Apia", relocated.TimeZoneId);
        Assert.Equal("VAGST", relocated.TaxLabel);

        var auditEvents = await test.Db.AuditEvents
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .ToListAsync();
        Assert.Equal(
            ["OrganisationSettingsUpdated", "OrganisationJurisdictionChanged"],
            auditEvents.Select(x => x.EventType));
        Assert.All(auditEvents, audit =>
        {
            Assert.Equal(test.UserId, audit.UserId);
            Assert.Equal(nameof(Organisation), audit.EntityType);
            Assert.Equal(test.Organisation.Id.ToString(), audit.EntityId);
        });

        using var settingsEvidence = JsonDocument.Parse(auditEvents[0].JsonData);
        Assert.Equal(
            "Accounting Test Limited",
            settingsEvidence.RootElement.GetProperty("Old").GetProperty("LegalName").GetString());
        Assert.Equal(
            "Updated Company Limited",
            settingsEvidence.RootElement.GetProperty("New").GetProperty("LegalName").GetString());
        Assert.Equal(
            20,
            settingsEvidence.RootElement.GetProperty("New").GetProperty("DefaultSupplierBillDueDays").GetInt32());

        using var jurisdictionEvidence = JsonDocument.Parse(auditEvents[1].JsonData);
        Assert.Equal(
            "FJ",
            jurisdictionEvidence.RootElement.GetProperty("Old").GetProperty("CountryCode").GetString());
        Assert.Equal(
            "WS",
            jurisdictionEvidence.RootElement.GetProperty("New").GetProperty("CountryCode").GetString());
        Assert.Equal(
            "WST",
            jurisdictionEvidence.RootElement.GetProperty("New").GetProperty("BaseCurrency").GetString());
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
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.UpdateProjectWipAccountsAsync(
                otherUserId,
                new UpdateProjectWipAccountsRequest(
                    test.Organisation.Id,
                    test.Account("1100").Id,
                    test.Account("2000").Id,
                    test.Account("4000").Id)));

        Assert.Equal(
            "Accounting Test Limited",
            await test.Db.Organisations
                .Where(x => x.Id == test.Organisation.Id)
                .Select(x => x.LegalName)
                .SingleAsync());
        Assert.Empty(await test.Db.AuditEvents.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task UnchangedSettings_DoNotCreateAuditNoise()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new OrganisationSettingsService(test.Db);
        var organisation = await test.Db.Organisations
            .AsNoTracking()
            .SingleAsync(x => x.Id == test.Organisation.Id);

        await service.UpdateAsync(
            test.UserId,
            new UpdateOrganisationSettingsRequest(
                organisation.Id,
                $" {organisation.LegalName} ",
                organisation.TradingName,
                organisation.Tin,
                organisation.ConversionDate,
                organisation.DefaultSalesInvoicePaymentTermType,
                organisation.DefaultSalesInvoiceDueDays,
                organisation.DefaultSupplierBillPaymentTermType,
                organisation.DefaultSupplierBillDueDays,
                organisation.RequireSupplierPaymentApproval,
                organisation.BusinessAddress,
                organisation.IsVatRegistered,
                organisation.VatRegistrationDate));
        await service.ChangeJurisdictionAsync(
            test.UserId,
            organisation.Id,
            organisation.CountryCode.ToLowerInvariant());

        Assert.Empty(await test.Db.AuditEvents.AsNoTracking().ToListAsync());
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
        Assert.Empty(await test.Db.AuditEvents.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task InvalidSettings_DoNotChangeOrganisationOrCreateAudit()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new OrganisationSettingsService(test.Db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateAsync(
                test.UserId,
                Request(test.Organisation.Id) with
                {
                    DefaultSupplierBillDueDays = 366
                }));

        var organisation = await test.Db.Organisations
            .AsNoTracking()
            .SingleAsync(x => x.Id == test.Organisation.Id);
        Assert.Equal("Accounting Test Limited", organisation.LegalName);
        Assert.Empty(await test.Db.AuditEvents.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task WipAccounts_MustBeActiveAccountsOfTheRequiredTypes()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new OrganisationSettingsService(test.Db);

        var wrongType = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateProjectWipAccountsAsync(
                test.UserId,
                new UpdateProjectWipAccountsRequest(
                    test.Organisation.Id,
                    test.Account("6000").Id,
                    test.Account("2000").Id,
                    test.Account("4000").Id)));
        Assert.Contains("asset account", wrongType.Message,
            StringComparison.OrdinalIgnoreCase);

        var bankAccount = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateProjectWipAccountsAsync(
                test.UserId,
                new UpdateProjectWipAccountsRequest(
                    test.Organisation.Id,
                    test.Account("1000").Id,
                    test.Account("2000").Id,
                    test.Account("4000").Id)));
        Assert.Contains("cannot be a bank", bankAccount.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await test.Db.AuditEvents.AsNoTracking().ToListAsync());
    }

    private static UpdateOrganisationSettingsRequest Request(
        Guid organisationId) =>
        new(
            organisationId,
            " Updated Company Limited ",
            " Updated Company ",
            " TIN-UPDATED ",
            new DateOnly(2026, 1, 1),
            PaymentTermType.DaysAfterDocumentDate,
            14,
            PaymentTermType.DayOfFollowingMonth,
            20,
            BusinessAddress: " Level 3, Test House, Suva ",
            IsVatRegistered: true,
            VatRegistrationDate: new DateOnly(2020, 1, 1));
}
