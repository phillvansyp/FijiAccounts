using System.Text.Json;
using FijiAccounts.Domain.Fiscalisation;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record UpdateFiscalisationConfigurationRequest(
    Guid OrganisationId,
    bool IsEnabled,
    FiscalPaymentType DefaultPaymentType,
    string? StandardTaxLabel,
    string? ZeroRatedTaxLabel,
    string? ExemptTaxLabel,
    string? OutOfScopeTaxLabel);

public sealed class FiscalisationConfigurationService(
    ApplicationDbContext db,
    TenantAccessService access,
    IWebHostEnvironment environment)
{
    public async Task<FiscalisationConfiguration?> GetAsync(
        string userId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        if (await access.FindAsync(userId, organisationId) is null)
        {
            throw new UnauthorizedAccessException(
                "You cannot view fiscalisation settings for this organisation.");
        }

        return await db.FiscalisationConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganisationId == organisationId, cancellationToken);
    }

    public async Task<FiscalisationConfiguration> UpdateAsync(
        string userId,
        UpdateFiscalisationConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await access.CanManageTeamAsync(userId, request.OrganisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot change fiscalisation settings for this organisation.");
        }

        var countryCode = await db.Organisations.AsNoTracking()
            .Where(x => x.Id == request.OrganisationId)
            .Select(x => x.CountryCode)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Organisation not found.");
        if (!countryCode.Equals("FJ", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "FRCS fiscalisation can only be configured for a Fiji organisation.");
        }
        if (!Enum.IsDefined(request.DefaultPaymentType))
        {
            throw new InvalidOperationException("Select a valid default payment type.");
        }

        var labels = new
        {
            Standard = Label(request.StandardTaxLabel),
            ZeroRated = Label(request.ZeroRatedTaxLabel),
            Exempt = Label(request.ExemptTaxLabel),
            OutOfScope = Label(request.OutOfScopeTaxLabel)
        };
        if (request.IsEnabled && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Fiscalisation cannot be enabled until an accredited SDC adapter is configured.");
        }
        if (request.IsEnabled && new[]
            {
                labels.Standard, labels.ZeroRated, labels.Exempt, labels.OutOfScope
            }.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException(
                "Enter a verified SDC label for every VAT treatment before enabling fiscalisation.");
        }

        var configuration = await db.FiscalisationConfigurations.SingleOrDefaultAsync(
            x => x.OrganisationId == request.OrganisationId,
            cancellationToken);
        var previous = configuration is null ? null : new
        {
            configuration.IsEnabled,
            Payment = configuration.DefaultPaymentType.ToString(),
            configuration.StandardTaxLabel,
            configuration.ZeroRatedTaxLabel,
            configuration.ExemptTaxLabel,
            configuration.OutOfScopeTaxLabel
        };
        configuration ??= new FiscalisationConfiguration
        {
            OrganisationId = request.OrganisationId
        };
        if (db.Entry(configuration).State == EntityState.Detached)
        {
            db.FiscalisationConfigurations.Add(configuration);
        }

        configuration.IsEnabled = request.IsEnabled;
        configuration.DefaultPaymentType = request.DefaultPaymentType;
        configuration.StandardTaxLabel = labels.Standard;
        configuration.ZeroRatedTaxLabel = labels.ZeroRated;
        configuration.ExemptTaxLabel = labels.Exempt;
        configuration.OutOfScopeTaxLabel = labels.OutOfScope;
        configuration.UpdatedAt = DateTimeOffset.UtcNow;
        configuration.UpdatedByUserId = userId;

        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = request.OrganisationId,
            EventType = "FiscalisationConfigurationUpdated",
            EntityType = nameof(FiscalisationConfiguration),
            EntityId = request.OrganisationId.ToString(),
            UserId = userId,
            JsonData = JsonSerializer.Serialize(new
            {
                Old = previous,
                New = new
                {
                    configuration.IsEnabled,
                    Payment = configuration.DefaultPaymentType.ToString(),
                    configuration.StandardTaxLabel,
                    configuration.ZeroRatedTaxLabel,
                    configuration.ExemptTaxLabel,
                    configuration.OutOfScopeTaxLabel,
                    Simulator = environment.IsDevelopment()
                }
            })
        });
        await db.SaveChangesAsync(cancellationToken);
        return configuration;
    }

    public static IReadOnlyDictionary<VatTreatment, IReadOnlyCollection<string>>
        TaxLabels(FiscalisationConfiguration configuration) =>
        new Dictionary<VatTreatment, IReadOnlyCollection<string>>
        {
            [VatTreatment.Standard] = Values(configuration.StandardTaxLabel),
            [VatTreatment.ZeroRated] = Values(configuration.ZeroRatedTaxLabel),
            [VatTreatment.Exempt] = Values(configuration.ExemptTaxLabel),
            [VatTreatment.OutOfScope] = Values(configuration.OutOfScopeTaxLabel)
        };

    private static string? Label(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var result = value.Trim();
        if (result.Length > 80)
        {
            throw new InvalidOperationException("SDC tax labels cannot exceed 80 characters.");
        }
        return result;
    }

    private static IReadOnlyCollection<string> Values(string? value) =>
        string.IsNullOrWhiteSpace(value) ? [] : [value];
}
