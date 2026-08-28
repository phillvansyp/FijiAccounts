using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record VatTurnoverAssessment(
    DateOnly From,
    DateOnly To,
    decimal TaxableTurnover,
    decimal RegistrationThreshold,
    bool IsVatRegistered)
{
    public decimal ThresholdPercentage => RegistrationThreshold == 0
        ? 0
        : Math.Round(TaxableTurnover / RegistrationThreshold * 100m, 1);

    public decimal RemainingBeforeThreshold =>
        Math.Max(0m, RegistrationThreshold - TaxableTurnover);

    public bool IsApproachingThreshold =>
        !IsVatRegistered && TaxableTurnover >= RegistrationThreshold * 0.8m;

    public bool RequiresRegistration =>
        !IsVatRegistered && TaxableTurnover > RegistrationThreshold;
}

public sealed class VatTurnoverMonitorService(
    ApplicationDbContext db,
    NotificationService notifications)
{
    public const decimal FijiRegistrationThreshold = 100_000m;

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
                    v.VoidDate >= periodStart &&
                    v.VoidDate <= periodEnd))
            .SumAsync(x => (decimal?)x.NetAmount, ct) ?? 0m;

        var credits = await db.SalesCreditNotes
            .AsNoTracking()
            .Where(x =>
                x.OrganisationId == organisationId &&
                x.CreditDate >= periodStart &&
                x.CreditDate <= periodEnd)
            .SumAsync(x => (decimal?)x.Subtotal, ct) ?? 0m;

        var creditReversals = await db.SalesCreditNoteReversals
            .AsNoTracking()
            .Where(x =>
                x.OrganisationId == organisationId &&
                x.ReversalDate >= periodStart &&
                x.ReversalDate <= periodEnd)
            .SumAsync(x => (decimal?)x.SalesCreditNote.Subtotal, ct) ?? 0m;

        return new VatTurnoverAssessment(
            periodStart,
            periodEnd,
            Math.Max(0m, sales - voids - credits + creditReversals),
            FijiRegistrationThreshold,
            organisation.IsVatRegistered);
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

        var shouldAlert =
            organisation.CountryCode.Equals("FJ", StringComparison.OrdinalIgnoreCase) &&
            assessment.IsApproachingThreshold;

        var severity = assessment.RequiresRegistration
            ? NotificationSeverity.Critical
            : NotificationSeverity.Warning;

        if (!shouldAlert)
        {
            await ResolveAsync(existing, organisationId, ct);
            return assessment;
        }

        var title = assessment.RequiresRegistration
            ? "VAT registration threshold exceeded"
            : "VAT registration threshold approaching";

        var message = assessment.RequiresRegistration
            ? $"Taxable turnover for the 12 months to {assessment.To:dd MMM yyyy} is FJD {assessment.TaxableTurnover:N2}. FRCS guidance requires VAT registration within 21 consecutive days after turnover exceeds FJD {assessment.RegistrationThreshold:N2}."
            : $"Taxable turnover for the 12 months to {assessment.To:dd MMM yyyy} is FJD {assessment.TaxableTurnover:N2} ({assessment.ThresholdPercentage:N1}% of the FJD {assessment.RegistrationThreshold:N2} registration threshold).";

        if (existing.Count == 1 && existing[0].Severity == severity)
        {
            var current = existing[0];
            var changed =
                current.Title != title ||
                current.Message != message ||
                current.Amount != assessment.TaxableTurnover ||
                current.Currency != organisation.BaseCurrency;

            if (changed)
            {
                current.Title = title;
                current.Message = message;
                current.Amount = assessment.TaxableTurnover;
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
                assessment.TaxableTurnover,
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
