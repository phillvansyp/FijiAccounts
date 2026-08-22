using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record UpdateOrganisationSettingsRequest(
    Guid OrganisationId,
    string LegalName,
    string? TradingName,
    string? Tin,
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

        var changed =
            await db.Organisations
                .Where(x => x.Id == request.OrganisationId)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.LegalName, legalName)
                    .SetProperty(x => x.TradingName, tradingName)
                    .SetProperty(x => x.Tin, tin)
                    .SetProperty(
                        x => x.DefaultSalesInvoicePaymentTermType,
                        request.DefaultSalesInvoicePaymentTermType)
                    .SetProperty(
                        x => x.DefaultSalesInvoiceDueDays,
                        request.DefaultSalesInvoiceDueDays)
                    .SetProperty(
                        x => x.DefaultSupplierBillPaymentTermType,
                        request.DefaultSupplierBillPaymentTermType)
                    .SetProperty(
                        x => x.DefaultSupplierBillDueDays,
                        request.DefaultSupplierBillDueDays),
                    cancellationToken);

        if (changed != 1)
        {
            throw new InvalidOperationException(
                "The organisation could not be updated.");
        }

        return await GetOrganisationAsync(
            request.OrganisationId,
            cancellationToken);
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
        var changed =
            await db.Organisations
                .Where(x => x.Id == organisationId)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.CountryCode, jurisdiction.CountryCode)
                    .SetProperty(x => x.BaseCurrency, jurisdiction.CurrencyCode)
                    .SetProperty(x => x.TimeZoneId, jurisdiction.TimeZoneId)
                    .SetProperty(x => x.TaxLabel, jurisdiction.TaxLabel)
                    .SetProperty(
                        x => x.FinancialYearEndMonth,
                        jurisdiction.FinancialYearEndMonth)
                    .SetProperty(
                        x => x.FinancialYearEndDay,
                        jurisdiction.FinancialYearEndDay),
                    cancellationToken);

        if (changed != 1)
        {
            throw new InvalidOperationException(
                "The organisation could not be updated.");
        }

        return await GetOrganisationAsync(organisationId, cancellationToken);
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

    private async Task<Organisation> GetOrganisationAsync(
        Guid organisationId,
        CancellationToken cancellationToken) =>
        await db.Organisations
            .AsNoTracking()
            .SingleAsync(x => x.Id == organisationId, cancellationToken);

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
