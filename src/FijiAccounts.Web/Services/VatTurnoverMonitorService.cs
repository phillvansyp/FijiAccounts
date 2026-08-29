using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FijiAccounts.Web.Services;

public sealed record VatTurnoverAssessment(
    DateOnly From,
    DateOnly To,
    decimal TaxableTurnover,
    decimal? ExpectedTaxableTurnoverNext12Months,
    DateTimeOffset? ForecastUpdatedAt,
    decimal RegistrationThreshold,
    bool IsVatRegistered)
{
    public decimal ThresholdPercentage => RegistrationThreshold == 0
        ? 0
        : Math.Round(TaxableTurnover / RegistrationThreshold * 100m, 1);

    public decimal RemainingBeforeThreshold =>
        Math.Max(0m, RegistrationThreshold - TaxableTurnover);

    public decimal? ForecastThresholdPercentage =>
        ExpectedTaxableTurnoverNext12Months is null || RegistrationThreshold == 0
            ? null
            : Math.Round(
                ExpectedTaxableTurnoverNext12Months.Value /
                RegistrationThreshold * 100m,
                1);

    public decimal AlertTurnover =>
        Math.Max(TaxableTurnover, ExpectedTaxableTurnoverNext12Months ?? 0m);

    public bool HistoricalRequiresRegistration =>
        !IsVatRegistered && TaxableTurnover >= RegistrationThreshold;

    public bool ForecastRequiresRegistration =>
        !IsVatRegistered &&
        ExpectedTaxableTurnoverNext12Months >= RegistrationThreshold;

    public bool ForecastIsApproachingThreshold =>
        !IsVatRegistered &&
        ExpectedTaxableTurnoverNext12Months >= RegistrationThreshold * 0.8m;

    public bool IsApproachingThreshold =>
        !IsVatRegistered &&
        (TaxableTurnover >= RegistrationThreshold * 0.8m ||
         ForecastIsApproachingThreshold);

    public bool RequiresRegistration =>
        HistoricalRequiresRegistration || ForecastRequiresRegistration;
}

public sealed record UpdateVatTurnoverForecastRequest(
    Guid OrganisationId,
    decimal? ExpectedTaxableTurnoverNext12Months);

public sealed class VatTurnoverMonitorService(
    ApplicationDbContext db,
    NotificationService notifications,
    TenantAccessService access)
{
    public const decimal FijiRegistrationThreshold = 100_000m;
    public const decimal NewZealandRegistrationThreshold = 60_000m;
    public const decimal MaximumForecastTurnover = 1_000_000_000_000m;

    public async Task<VatTurnoverAssessment> GetAssessmentAsync(
        Guid organisationId,
        DateOnly asOf,
        CancellationToken ct = default)
    {
        var organisation = await db.Organisations
            .AsNoTracking()
            .SingleAsync(x => x.Id == organisationId, ct);

        var periodEnd = new DateOnly(asOf.Year, asOf.Month, 1).AddDays(-1);
        var periodStart = periodEnd.AddMonths(-12).AddDays(1);

        var sales = await db.SalesInvoiceLines
            .AsNoTracking()
            .Where(x =>
                x.SalesInvoice.OrganisationId == organisationId &&
                x.SalesInvoice.IssueDate >= periodStart &&
                x.SalesInvoice.IssueDate <= periodEnd &&
                x.SalesInvoice.Status != InvoiceStatus.Draft &&
                (x.VatTreatment == VatTreatment.Standard ||
                 x.VatTreatment == VatTreatment.ZeroRated))
            .SumAsync(x => (decimal?)x.NetAmount, ct) ?? 0m;

        var voids = await db.SalesInvoiceLines
            .AsNoTracking()
            .Where(x =>
                x.SalesInvoice.OrganisationId == organisationId &&
                (x.VatTreatment == VatTreatment.Standard ||
                 x.VatTreatment == VatTreatment.ZeroRated) &&
                db.SalesInvoiceVoids.Any(v =>
                    v.OrganisationId == organisationId &&
                    v.SalesInvoiceId == x.SalesInvoiceId &&
                    v.Status == SalesInvoiceVoidStatus.Posted &&
                    v.VoidDate >= periodStart &&
                    v.VoidDate <= periodEnd))
            .SumAsync(x => (decimal?)x.NetAmount, ct) ?? 0m;

        var credits = await db.SalesCreditNotes
            .AsNoTracking()
            .Where(x =>
                x.OrganisationId == organisationId &&
                x.Status == SalesCreditNoteStatus.Posted &&
                x.CreditDate >= periodStart &&
                x.CreditDate <= periodEnd)
            .SumAsync(x => (decimal?)x.Subtotal, ct) ?? 0m;

        var creditReversals = await db.SalesCreditNoteReversals
            .AsNoTracking()
            .Where(x =>
                x.OrganisationId == organisationId &&
                x.Status == SalesCreditNoteReversalStatus.Posted &&
                x.ReversalDate >= periodStart &&
                x.ReversalDate <= periodEnd)
            .SumAsync(x => (decimal?)x.SalesCreditNote.Subtotal, ct) ?? 0m;

        return new VatTurnoverAssessment(
            periodStart,
            periodEnd,
            Math.Max(0m, sales - voids - credits + creditReversals),
            organisation.ExpectedTaxableTurnoverNext12Months,
            organisation.VatTurnoverForecastUpdatedAt,
            organisation.CountryCode.Equals("NZ", StringComparison.OrdinalIgnoreCase)
                ? NewZealandRegistrationThreshold
                : FijiRegistrationThreshold,
            organisation.IsVatRegistered);
    }

    public async Task<VatTurnoverAssessment> UpdateForecastAsync(
        string userId,
        UpdateVatTurnoverForecastRequest request,
        DateOnly asOf,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, request.OrganisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot update the VAT turnover forecast for this organisation.");
        }

        if (request.ExpectedTaxableTurnoverNext12Months is < 0m or > MaximumForecastTurnover)
        {
            throw new InvalidOperationException(
                $"Expected taxable turnover must be between 0.00 and {MaximumForecastTurnover:N2}.");
        }

        var organisation = await db.Organisations.SingleAsync(
            x => x.Id == request.OrganisationId,
            ct);
        if (!organisation.CountryCode.Equals("FJ", StringComparison.OrdinalIgnoreCase) &&
            !organisation.CountryCode.Equals("NZ", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The indirect-tax turnover forecast is available only to Fiji and New Zealand organisations.");
        }

        var previous = organisation.ExpectedTaxableTurnoverNext12Months;
        var updated = request.ExpectedTaxableTurnoverNext12Months;
        if (previous != updated)
        {
            var updatedAt = DateTimeOffset.UtcNow;
            organisation.ExpectedTaxableTurnoverNext12Months = updated;
            organisation.VatTurnoverForecastUpdatedAt = updatedAt;
            organisation.VatTurnoverForecastUpdatedByUserId = userId;
            db.AuditEvents.Add(new AuditEvent
            {
                OrganisationId = organisation.Id,
                UserId = userId,
                EventType = "VatTurnoverForecastUpdated",
                EntityType = nameof(Organisation),
                EntityId = organisation.Id.ToString(),
                JsonData = JsonSerializer.Serialize(new
                {
                    PreviousExpectedTaxableTurnoverNext12Months = previous,
                    ExpectedTaxableTurnoverNext12Months = updated,
                    updatedAt
                })
            });
            await db.SaveChangesAsync(ct);
        }

        return (await RefreshAlertAsync(organisation.Id, asOf, ct))!;
    }

    public async Task<VatTurnoverAssessment?> RefreshAlertAsync(
        Guid organisationId,
        DateOnly asOf,
        CancellationToken ct = default)
    {
        var organisation = await db.Organisations
            .SingleAsync(x => x.Id == organisationId, ct);

        var assessment = await GetAssessmentAsync(organisationId, asOf, ct);
        var existing = await db.Notifications
            .Where(x =>
                x.OrganisationId == organisationId &&
                x.Type == NotificationType.VatRegistration &&
                x.RelatedEntityType == nameof(Organisation) &&
                x.RelatedEntityId == organisationId.ToString() &&
                x.Status != NotificationStatus.Resolved)
            .ToListAsync(ct);

        var supported = organisation.CountryCode.Equals("FJ", StringComparison.OrdinalIgnoreCase) ||
                        organisation.CountryCode.Equals("NZ", StringComparison.OrdinalIgnoreCase);
        var shouldAlert = supported && assessment.IsApproachingThreshold;

        var severity = assessment.RequiresRegistration
            ? NotificationSeverity.Critical
            : NotificationSeverity.Warning;

        if (!shouldAlert)
        {
            await ResolveAsync(existing, organisationId, ct);
            return assessment;
        }

        var taxLabel = organisation.TaxLabel;
        var authority = organisation.CountryCode.Equals("NZ", StringComparison.OrdinalIgnoreCase)
            ? "Inland Revenue or a New Zealand tax adviser"
            : "FRCS or a Fiji tax practitioner";
        var registrationAction = organisation.CountryCode.Equals("NZ", StringComparison.OrdinalIgnoreCase)
            ? $"Confirm the registration position with {authority}."
            : $"Registration may be required within 21 consecutive days. Confirm the registration position with {authority}.";
        var title = assessment.RequiresRegistration
            ? $"{taxLabel} registration threshold reached"
            : $"{taxLabel} registration threshold approaching";

        var message = assessment.ForecastRequiresRegistration &&
                      !assessment.HistoricalRequiresRegistration
            ? $"Expected taxable turnover for the next 12 months is {organisation.BaseCurrency} {assessment.ExpectedTaxableTurnoverNext12Months:N2}, above the {organisation.BaseCurrency} {assessment.RegistrationThreshold:N2} {taxLabel} registration threshold. {registrationAction}"
            : assessment.RequiresRegistration
                ? $"Taxable turnover for the 12 months to {assessment.To:dd MMM yyyy} is {organisation.BaseCurrency} {assessment.TaxableTurnover:N2}, at or above the {organisation.BaseCurrency} {assessment.RegistrationThreshold:N2} {taxLabel} registration threshold. {registrationAction}"
                : assessment.ForecastIsApproachingThreshold &&
                  assessment.ExpectedTaxableTurnoverNext12Months > assessment.TaxableTurnover
                    ? $"Expected taxable turnover for the next 12 months is {organisation.BaseCurrency} {assessment.ExpectedTaxableTurnoverNext12Months:N2} ({assessment.ForecastThresholdPercentage:N1}% of the {organisation.BaseCurrency} {assessment.RegistrationThreshold:N2} registration threshold)."
                    : $"Taxable turnover for the 12 months to {assessment.To:dd MMM yyyy} is {organisation.BaseCurrency} {assessment.TaxableTurnover:N2} ({assessment.ThresholdPercentage:N1}% of the {organisation.BaseCurrency} {assessment.RegistrationThreshold:N2} registration threshold).";

        if (existing.Count == 1 && existing[0].Severity == severity)
        {
            var current = existing[0];
            var changed =
                current.Title != title ||
                current.Message != message ||
                current.Amount != assessment.AlertTurnover ||
                current.Currency != organisation.BaseCurrency;

            if (changed)
            {
                current.Title = title;
                current.Message = message;
                current.Amount = assessment.AlertTurnover;
                current.Currency = organisation.BaseCurrency;
                await db.SaveChangesAsync(ct);
                notifications.PublishOrganisationUpdate(organisationId);
            }

            return assessment;
        }

        await ResolveAsync(existing, organisationId, ct);

        await notifications.CreateAsync(
            new CreateNotificationRequest(
                organisationId,
                title,
                message,
                NotificationType.VatRegistration,
                severity,
                nameof(Organisation),
                organisationId.ToString(),
                assessment.AlertTurnover,
                organisation.BaseCurrency),
            ct);

        return assessment;
    }

    private async Task ResolveAsync(
        List<Notification> existing,
        Guid organisationId,
        CancellationToken ct)
    {
        if (existing.Count == 0)
        {
            return;
        }

        var resolvedAt = DateTimeOffset.UtcNow;
        foreach (var notification in existing)
        {
            notification.Status = NotificationStatus.Resolved;
            notification.ResolvedAt = resolvedAt;
            notification.IsRead = true;
            notification.ReadAt = resolvedAt;
        }

        await db.SaveChangesAsync(ct);
        notifications.PublishOrganisationUpdate(organisationId);
    }
}
