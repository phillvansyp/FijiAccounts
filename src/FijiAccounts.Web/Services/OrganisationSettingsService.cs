using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record UpdateOrganisationSettingsRequest(
    Guid OrganisationId,
    string LegalName,
    string? TradingName,
    string? Tin,
    DateOnly? ConversionDate,
    PaymentTermType DefaultSalesInvoicePaymentTermType,
    int DefaultSalesInvoiceDueDays,
    PaymentTermType DefaultSupplierBillPaymentTermType,
    int DefaultSupplierBillDueDays);

public sealed class OrganisationSettingsService(
    ApplicationDbContext db)
{
    public async Task<Organisation> UpdateAsync(
        string userId,
        UpdateOrganisationSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireManagerAsync(
            userId,
            request.OrganisationId,
            cancellationToken);

        var legalName = RequiredText(request.LegalName, 160, "legal name");
        var tradingName = OptionalText(request.TradingName, 80, "trading name");
        var tin = OptionalText(request.Tin, 32, "tax identification number");
        ValidatePaymentTerm(
            request.DefaultSalesInvoicePaymentTermType,
            request.DefaultSalesInvoiceDueDays,
            "customer invoice");
        ValidatePaymentTerm(
            request.DefaultSupplierBillPaymentTermType,
            request.DefaultSupplierBillDueDays,
            "supplier bill");

        var organisation = await db.Organisations.SingleOrDefaultAsync(
            x => x.Id == request.OrganisationId,
            cancellationToken);
        if (organisation is null)
        {
            throw new InvalidOperationException(
                "The organisation could not be updated.");
        }

        var previous = new
        {
            organisation.LegalName,
            organisation.TradingName,
            organisation.Tin,
            organisation.ConversionDate,
            DefaultSalesInvoicePaymentTermType =
                organisation.DefaultSalesInvoicePaymentTermType.ToString(),
            organisation.DefaultSalesInvoiceDueDays,
            DefaultSupplierBillPaymentTermType =
                organisation.DefaultSupplierBillPaymentTermType.ToString(),
            organisation.DefaultSupplierBillDueDays
        };
        var updated = new
        {
            LegalName = legalName,
            TradingName = tradingName,
            Tin = tin,
            request.ConversionDate,
            DefaultSalesInvoicePaymentTermType =
                request.DefaultSalesInvoicePaymentTermType.ToString(),
            request.DefaultSalesInvoiceDueDays,
            DefaultSupplierBillPaymentTermType =
                request.DefaultSupplierBillPaymentTermType.ToString(),
            request.DefaultSupplierBillDueDays
        };
        if (previous.Equals(updated))
        {
            return organisation;
        }

        organisation.LegalName = legalName;
        organisation.TradingName = tradingName;
        organisation.Tin = tin;
        organisation.ConversionDate = request.ConversionDate;
        organisation.DefaultSalesInvoicePaymentTermType =
            request.DefaultSalesInvoicePaymentTermType;
        organisation.DefaultSalesInvoiceDueDays =
            request.DefaultSalesInvoiceDueDays;
        organisation.DefaultSupplierBillPaymentTermType =
            request.DefaultSupplierBillPaymentTermType;
        organisation.DefaultSupplierBillDueDays =
            request.DefaultSupplierBillDueDays;

        db.AuditEvents.Add(CreateAuditEvent(
            request.OrganisationId,
            userId,
            "OrganisationSettingsUpdated",
            new { Old = previous, New = updated }));
        await db.SaveChangesAsync(cancellationToken);

        return organisation;
    }

    public async Task<Organisation> ChangeJurisdictionAsync(
        string userId,
        Guid organisationId,
        string countryCode,
        CancellationToken cancellationToken = default)
    {
        await RequireManagerAsync(userId, organisationId, cancellationToken);

        if (await db.PostedJournals.AnyAsync(
                x => x.OrganisationId == organisationId,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "The jurisdiction cannot be changed after accounting transactions have been posted.");
        }

        var jurisdiction = IslandJurisdictions.Get(countryCode);
        var organisation = await db.Organisations.SingleOrDefaultAsync(
            x => x.Id == organisationId,
            cancellationToken);
        if (organisation is null)
        {
            throw new InvalidOperationException(
                "The organisation could not be updated.");
        }

        var previous = new
        {
            organisation.CountryCode,
            organisation.BaseCurrency,
            organisation.TimeZoneId,
            organisation.TaxLabel,
            organisation.FinancialYearEndMonth,
            organisation.FinancialYearEndDay
        };
        var updated = new
        {
            CountryCode = jurisdiction.CountryCode,
            BaseCurrency = jurisdiction.CurrencyCode,
            TimeZoneId = jurisdiction.TimeZoneId,
            TaxLabel = jurisdiction.TaxLabel,
            jurisdiction.FinancialYearEndMonth,
            jurisdiction.FinancialYearEndDay
        };
        if (previous.Equals(updated))
        {
            return organisation;
        }

        organisation.CountryCode = jurisdiction.CountryCode;
        organisation.BaseCurrency = jurisdiction.CurrencyCode;
        organisation.TimeZoneId = jurisdiction.TimeZoneId;
        organisation.TaxLabel = jurisdiction.TaxLabel;
        organisation.FinancialYearEndMonth =
            jurisdiction.FinancialYearEndMonth;
        organisation.FinancialYearEndDay =
            jurisdiction.FinancialYearEndDay;

        db.AuditEvents.Add(CreateAuditEvent(
            organisationId,
            userId,
            "OrganisationJurisdictionChanged",
            new { Old = previous, New = updated }));
        await db.SaveChangesAsync(cancellationToken);

        return organisation;
    }

    private async Task RequireManagerAsync(
        string userId,
        Guid organisationId,
        CancellationToken cancellationToken)
    {
        var canManage =
            await db.OrganisationMemberships
                .AsNoTracking()
                .AnyAsync(x =>
                    x.UserId == userId &&
                    x.OrganisationId == organisationId &&
                    (x.Role == OrganisationRole.Owner ||
                     x.Role == OrganisationRole.Administrator),
                    cancellationToken);

        if (!canManage)
        {
            throw new UnauthorizedAccessException(
                "You do not have permission to change this organisation's settings.");
        }
    }

    private static AuditEvent CreateAuditEvent(
        Guid organisationId,
        string userId,
        string eventType,
        object evidence) =>
        new()
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = eventType,
            EntityType = nameof(Organisation),
            EntityId = organisationId.ToString(),
            JsonData = JsonSerializer.Serialize(evidence)
        };

    private static string RequiredText(
        string value,
        int maximumLength,
        string fieldName)
    {
        var result = value.Trim();
        if (result.Length is < 1 || result.Length > maximumLength)
        {
            throw new InvalidOperationException(
                $"Enter a {fieldName} between 1 and {maximumLength} characters.");
        }

        return result;
    }

    private static string? OptionalText(
        string? value,
        int maximumLength,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var result = value.Trim();
        if (result.Length > maximumLength)
        {
            throw new InvalidOperationException(
                $"Enter a {fieldName} no longer than {maximumLength} characters.");
        }

        return result;
    }

    private static void ValidatePaymentTerm(
        PaymentTermType type,
        int value,
        string fieldName)
    {
        if (!Enum.IsDefined(type) || value is < 0 or > 365)
        {
            throw new InvalidOperationException(
                $"Enter a valid {fieldName} payment term.");
        }
    }
}
