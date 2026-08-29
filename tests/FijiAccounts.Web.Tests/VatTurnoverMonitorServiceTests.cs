using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class VatTurnoverMonitorServiceTests
{
    [Fact]
    public async Task Assessment_IncludesTaxableSuppliesAndExcludesExemptSupplies()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.IsVatRegistered = false;
        AddInvoice(test, new DateOnly(2026, 7, 10), 75_000m, VatTreatment.Standard);
        AddInvoice(test, new DateOnly(2026, 6, 10), 20_000m, VatTreatment.ZeroRated);
        AddInvoice(test, new DateOnly(2026, 5, 10), 40_000m, VatTreatment.Exempt);
        AddInvoice(test, new DateOnly(2025, 6, 10), 50_000m, VatTreatment.Standard);
        await test.Db.SaveChangesAsync();

        var service = new VatTurnoverMonitorService(test.Db, test.Notifications, test.Access);
        var assessment = await service.GetAssessmentAsync(
            test.Organisation.Id,
            new DateOnly(2026, 8, 28));

        Assert.Equal(new DateOnly(2025, 8, 1), assessment.From);
        Assert.Equal(new DateOnly(2026, 7, 31), assessment.To);
        Assert.Equal(95_000m, assessment.TaxableTurnover);
        Assert.True(assessment.IsApproachingThreshold);
        Assert.False(assessment.RequiresRegistration);
    }

    [Fact]
    public async Task RefreshAlert_CreatesCriticalAlertAboveThreshold()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.IsVatRegistered = false;
        AddInvoice(test, new DateOnly(2026, 7, 10), 100_001m, VatTreatment.Standard);
        await test.Db.SaveChangesAsync();

        var service = new VatTurnoverMonitorService(test.Db, test.Notifications, test.Access);
        await service.RefreshAlertAsync(test.Organisation.Id, new DateOnly(2026, 8, 28));

        var alert = await test.Db.Notifications.SingleAsync(
            x => x.Type == NotificationType.VatRegistration);
        Assert.Equal(NotificationSeverity.Critical, alert.Severity);
        Assert.Contains("within 21 consecutive days", alert.Message);
        Assert.Equal(100_001m, alert.Amount);
    }

    [Fact]
    public async Task RefreshAlert_ResolvesAlertAfterVatRegistration()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.IsVatRegistered = false;
        AddInvoice(test, new DateOnly(2026, 7, 10), 90_000m, VatTreatment.Standard);
        await test.Db.SaveChangesAsync();

        var service = new VatTurnoverMonitorService(test.Db, test.Notifications, test.Access);
        await service.RefreshAlertAsync(test.Organisation.Id, new DateOnly(2026, 8, 28));
        test.Organisation.IsVatRegistered = true;
        await test.Db.SaveChangesAsync();
        await service.RefreshAlertAsync(test.Organisation.Id, new DateOnly(2026, 8, 28));

        var alert = await test.Db.Notifications.SingleAsync(
            x => x.Type == NotificationType.VatRegistration);
        Assert.Equal(NotificationStatus.Resolved, alert.Status);
        Assert.True(alert.IsRead);
    }

    [Fact]
    public async Task RefreshAlert_UpdatesAnOpenAlertWhenTurnoverChangesWithinSeverity()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.IsVatRegistered = false;
        AddInvoice(test, new DateOnly(2026, 6, 10), 85_000m, VatTreatment.Standard);
        await test.Db.SaveChangesAsync();

        var service = new VatTurnoverMonitorService(test.Db, test.Notifications, test.Access);
        await service.RefreshAlertAsync(test.Organisation.Id, new DateOnly(2026, 8, 28));

        AddInvoice(test, new DateOnly(2026, 7, 10), 5_000m, VatTreatment.ZeroRated);
        await test.Db.SaveChangesAsync();
        await service.RefreshAlertAsync(test.Organisation.Id, new DateOnly(2026, 8, 28));

        var alerts = await test.Db.Notifications
            .Where(x => x.Type == NotificationType.VatRegistration)
            .ToListAsync();
        var alert = Assert.Single(alerts);
        Assert.Equal(NotificationStatus.Open, alert.Status);
        Assert.Equal(90_000m, alert.Amount);
        Assert.Contains("90,000.00", alert.Message);
    }

    [Fact]
    public async Task Assessment_ForecastCanTriggerForwardLookingRegistrationWarning()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.IsVatRegistered = false;
        test.Organisation.ExpectedTaxableTurnoverNext12Months = 125_000m;
        test.Organisation.VatTurnoverForecastUpdatedAt = DateTimeOffset.UtcNow;
        await test.Db.SaveChangesAsync();

        var service = new VatTurnoverMonitorService(test.Db, test.Notifications, test.Access);
        var assessment = await service.GetAssessmentAsync(
            test.Organisation.Id,
            new DateOnly(2026, 8, 28));

        Assert.Equal(125_000m, assessment.ExpectedTaxableTurnoverNext12Months);
        Assert.Equal(125m, assessment.ForecastThresholdPercentage);
        Assert.True(assessment.ForecastRequiresRegistration);
        Assert.True(assessment.RequiresRegistration);
        Assert.False(assessment.HistoricalRequiresRegistration);
    }

    [Fact]
    public async Task UpdateForecast_AuditsAndRefreshesTheRegistrationAlert()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.IsVatRegistered = false;
        await test.Db.SaveChangesAsync();

        var service = new VatTurnoverMonitorService(test.Db, test.Notifications, test.Access);
        var assessment = await service.UpdateForecastAsync(
            test.UserId,
            new UpdateVatTurnoverForecastRequest(test.Organisation.Id, 110_000m),
            new DateOnly(2026, 8, 28));

        Assert.True(assessment.ForecastRequiresRegistration);
        var organisation = await test.Db.Organisations
            .AsNoTracking()
            .SingleAsync(x => x.Id == test.Organisation.Id);
        Assert.Equal(110_000m, organisation.ExpectedTaxableTurnoverNext12Months);
        Assert.Equal(test.UserId, organisation.VatTurnoverForecastUpdatedByUserId);
        Assert.NotNull(organisation.VatTurnoverForecastUpdatedAt);
        Assert.True(await test.Db.AuditEvents.AnyAsync(x =>
            x.EventType == "VatTurnoverForecastUpdated" &&
            x.OrganisationId == test.Organisation.Id));
        var alert = await test.Db.Notifications.SingleAsync(x =>
            x.Type == NotificationType.VatRegistration);
        Assert.Equal(NotificationSeverity.Critical, alert.Severity);
        Assert.Equal(110_000m, alert.Amount);
        Assert.Contains("Expected taxable turnover", alert.Message);
    }

    [Fact]
    public async Task UpdateForecast_RejectsUsersWithoutPostingAccess()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new VatTurnoverMonitorService(test.Db, test.Notifications, test.Access);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.UpdateForecastAsync(
                "not-a-member",
                new UpdateVatTurnoverForecastRequest(test.Organisation.Id, 90_000m),
                new DateOnly(2026, 8, 28)));

        Assert.Null(test.Organisation.ExpectedTaxableTurnoverNext12Months);
    }

    [Fact]
    public async Task UpdateForecast_ClearingEstimateResolvesForecastOnlyAlert()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.IsVatRegistered = false;
        await test.Db.SaveChangesAsync();
        var service = new VatTurnoverMonitorService(test.Db, test.Notifications, test.Access);

        await service.UpdateForecastAsync(
            test.UserId,
            new UpdateVatTurnoverForecastRequest(test.Organisation.Id, 110_000m),
            new DateOnly(2026, 8, 28));
        var assessment = await service.UpdateForecastAsync(
            test.UserId,
            new UpdateVatTurnoverForecastRequest(test.Organisation.Id, null),
            new DateOnly(2026, 8, 28));

        Assert.Null(assessment.ExpectedTaxableTurnoverNext12Months);
        Assert.False(assessment.IsApproachingThreshold);
        var alert = await test.Db.Notifications.SingleAsync(x =>
            x.Type == NotificationType.VatRegistration);
        Assert.Equal(NotificationStatus.Resolved, alert.Status);
        Assert.Equal(2, await test.Db.AuditEvents.CountAsync(x =>
            x.EventType == "VatTurnoverForecastUpdated"));
    }

    [Fact]
    public async Task UpdateForecast_RejectsNegativeEstimateWithoutAudit()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new VatTurnoverMonitorService(test.Db, test.Notifications, test.Access);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateForecastAsync(
                test.UserId,
                new UpdateVatTurnoverForecastRequest(test.Organisation.Id, -1m),
                new DateOnly(2026, 8, 28)));

        Assert.Null(test.Organisation.ExpectedTaxableTurnoverNext12Months);
        Assert.False(await test.Db.AuditEvents.AnyAsync(x =>
            x.EventType == "VatTurnoverForecastUpdated"));
    }

    private static void AddInvoice(
        AccountingTestDatabase test,
        DateOnly issueDate,
        decimal netAmount,
        VatTreatment treatment)
    {
        var invoice = new SalesInvoice
        {
            OrganisationId = test.Organisation.Id,
            Organisation = test.Organisation,
            CustomerId = test.Customer.Id,
            Customer = test.Customer,
            SequenceNumber = test.Db.SalesInvoices.Local.Count + 1,
            InvoiceNumber = $"TEST-{Guid.NewGuid():N}",
            IssueDate = issueDate,
            DueDate = issueDate.AddDays(30),
            Status = InvoiceStatus.Posted,
            Subtotal = netAmount,
            Total = netAmount,
            TransactionSubtotal = netAmount,
            TransactionTotal = netAmount,
            CreatedByUserId = test.UserId
        };
        invoice.Lines.Add(new SalesInvoiceLine
        {
            SalesInvoice = invoice,
            Description = "Turnover test",
            Quantity = 1m,
            UnitPrice = netAmount,
            TransactionUnitPrice = netAmount,
            VatTreatment = treatment,
            NetAmount = netAmount,
            GrossAmount = netAmount,
            TransactionNetAmount = netAmount,
            TransactionGrossAmount = netAmount,
            RevenueAccountId = test.Db.LedgerAccounts
                .Single(x => x.OrganisationId == test.Organisation.Id && x.Code == "4000").Id
        });
        test.Db.SalesInvoices.Add(invoice);
    }
}
