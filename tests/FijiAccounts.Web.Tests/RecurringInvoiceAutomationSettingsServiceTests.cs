using System.Text.Json;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class RecurringInvoiceAutomationSettingsServiceTests
{
    [Fact]
    public async Task Owner_CanUpdateAutomationSettingsWithAuditEvidence()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new RecurringInvoiceAutomationSettingsService(
            test.Db,
            test.Access);

        var updated = await service.UpdateAsync(
            test.UserId,
            new UpdateRecurringInvoiceAutomationSettingsRequest(
                test.Organisation.Id,
                false,
                new TimeOnly(6, 30)));

        Assert.False(updated.RecurringInvoiceAutomationEnabled);
        Assert.Equal(
            new TimeOnly(6, 30),
            updated.RecurringInvoiceAutomationTime);
        var audit = await test.Db.AuditEvents.AsNoTracking().SingleAsync();
        Assert.Equal("RecurringInvoiceAutomationSettingsUpdated", audit.EventType);
        Assert.Equal(nameof(Organisation), audit.EntityType);
        Assert.Equal(test.Organisation.Id.ToString(), audit.EntityId);
        Assert.Equal(test.UserId, audit.UserId);
        using var evidence = JsonDocument.Parse(audit.JsonData);
        Assert.True(
            evidence.RootElement.GetProperty("Old").GetProperty("Enabled").GetBoolean());
        Assert.False(
            evidence.RootElement.GetProperty("New").GetProperty("Enabled").GetBoolean());
        Assert.Equal(
            "06:30",
            evidence.RootElement.GetProperty("New").GetProperty("RunTime").GetString());
    }

    [Fact]
    public async Task UnchangedSettings_DoNotCreateAuditNoise()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new RecurringInvoiceAutomationSettingsService(
            test.Db,
            test.Access);

        await service.UpdateAsync(
            test.UserId,
            new UpdateRecurringInvoiceAutomationSettingsRequest(
                test.Organisation.Id,
                test.Organisation.RecurringInvoiceAutomationEnabled,
                test.Organisation.RecurringInvoiceAutomationTime));

        Assert.Empty(await test.Db.AuditEvents.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ReadOnlyMember_CannotUpdateSettingsOrCreateAudit()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new RecurringInvoiceAutomationSettingsService(
            test.Db,
            test.Access);
        await test.Db.OrganisationMemberships
            .Where(x =>
                x.UserId == test.UserId &&
                x.OrganisationId == test.Organisation.Id)
            .ExecuteUpdateAsync(update =>
                update.SetProperty(x => x.Role, OrganisationRole.ReadOnly));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.UpdateAsync(
                test.UserId,
                new UpdateRecurringInvoiceAutomationSettingsRequest(
                    test.Organisation.Id,
                    false,
                    new TimeOnly(6, 0))));

        Assert.True((await ReloadOrganisationAsync(test))
            .RecurringInvoiceAutomationEnabled);
        Assert.Empty(await test.Db.AuditEvents.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task OtherTenant_CannotBeTargetedWithCurrentMembership()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new RecurringInvoiceAutomationSettingsService(
            test.Db,
            test.Access);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.UpdateAsync(
                test.UserId,
                new UpdateRecurringInvoiceAutomationSettingsRequest(
                    Guid.NewGuid(),
                    true,
                    new TimeOnly(6, 0))));

        Assert.Empty(await test.Db.AuditEvents.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task SubMinuteTime_IsRejectedWithoutChangesOrAudit()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new RecurringInvoiceAutomationSettingsService(
            test.Db,
            test.Access);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateAsync(
                test.UserId,
                new UpdateRecurringInvoiceAutomationSettingsRequest(
                    test.Organisation.Id,
                    false,
                    new TimeOnly(6, 0, 30))));

        Assert.True((await ReloadOrganisationAsync(test))
            .RecurringInvoiceAutomationEnabled);
        Assert.Empty(await test.Db.AuditEvents.AsNoTracking().ToListAsync());
    }

    private static async Task<Organisation> ReloadOrganisationAsync(
        AccountingTestDatabase test) =>
        await test.Db.Organisations
            .AsNoTracking()
            .SingleAsync(x => x.Id == test.Organisation.Id);
}
