using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record CurrencyOption(string Code, string Name, bool IsBaseCurrency, bool IsActive);

public sealed class TransactionCurrencyService(
    ApplicationDbContext db,
    TenantAccessService access)
{
    private static readonly IReadOnlyDictionary<string, string> StandardCurrencies =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FJD"] = "Fiji dollar",
            ["USD"] = "United States dollar",
            ["AUD"] = "Australian dollar",
            ["NZD"] = "New Zealand dollar"
        };

    public async Task<IReadOnlyList<CurrencyOption>> ListAsync(
        string userId,
        Guid organisationId,
        CancellationToken ct = default)
    {
        var organisation = await RequireOrganisationAccessAsync(userId, organisationId, ct);
        var configured = await db.OrganisationCurrencies.AsNoTracking()
            .Where(x => x.OrganisationId == organisationId)
            .OrderBy(x => x.Code)
            .ToListAsync(ct);
        var results = StandardCurrencies
            .Select(x => new CurrencyOption(
                x.Key,
                x.Value,
                string.Equals(x.Key, organisation.BaseCurrency, StringComparison.OrdinalIgnoreCase),
                true))
            .ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);

        foreach (var item in configured)
        {
            results[item.Code] = new CurrencyOption(
                item.Code,
                item.Name,
                string.Equals(item.Code, organisation.BaseCurrency, StringComparison.OrdinalIgnoreCase),
                item.IsActive || string.Equals(item.Code, organisation.BaseCurrency, StringComparison.OrdinalIgnoreCase));
        }

        if (!results.ContainsKey(organisation.BaseCurrency))
        {
            results[organisation.BaseCurrency] = new CurrencyOption(
                organisation.BaseCurrency,
                organisation.BaseCurrency,
                true,
                true);
        }

        return results.Values
            .OrderByDescending(x => x.IsBaseCurrency)
            .ThenBy(x => x.Code)
            .ToList();
    }

    public async Task<OrganisationCurrency> AddAsync(
        string userId,
        Guid organisationId,
        string code,
        string name,
        CancellationToken ct = default)
    {
        await RequireManagementAccessAsync(userId, organisationId);
        var normalizedCode = NormalizeCode(code);
        var normalizedName = name.Trim();
        if (normalizedName.Length is < 1 or > 80)
        {
            throw new InvalidOperationException("Enter a currency name of 80 characters or fewer.");
        }

        var currency = await db.OrganisationCurrencies.SingleOrDefaultAsync(
            x => x.OrganisationId == organisationId && x.Code == normalizedCode,
            ct);
        if (currency is null)
        {
            currency = new OrganisationCurrency
            {
                OrganisationId = organisationId,
                Code = normalizedCode,
                Name = normalizedName,
                CreatedByUserId = userId
            };
            db.OrganisationCurrencies.Add(currency);
        }
        else
        {
            currency.Name = normalizedName;
            currency.IsActive = true;
        }

        await db.SaveChangesAsync(ct);
        return currency;
    }

    public async Task SetActiveAsync(
        string userId,
        Guid organisationId,
        string code,
        bool isActive,
        CancellationToken ct = default)
    {
        await RequireManagementAccessAsync(userId, organisationId);
        var organisation = await db.Organisations.SingleAsync(x => x.Id == organisationId, ct);
        var normalizedCode = NormalizeCode(code);
        if (!isActive && string.Equals(normalizedCode, organisation.BaseCurrency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The organisation base currency cannot be disabled.");
        }

        var currency = await db.OrganisationCurrencies.SingleOrDefaultAsync(
            x => x.OrganisationId == organisationId && x.Code == normalizedCode,
            ct);
        if (currency is null)
        {
            currency = new OrganisationCurrency
            {
                OrganisationId = organisationId,
                Code = normalizedCode,
                Name = StandardCurrencies.GetValueOrDefault(normalizedCode, normalizedCode),
                IsActive = isActive,
                CreatedByUserId = userId
            };
            db.OrganisationCurrencies.Add(currency);
        }
        else
        {
            currency.IsActive = isActive;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<TransactionExchangeRate> SaveRateAsync(
        string userId,
        Guid organisationId,
        string fromCurrency,
        DateOnly effectiveDate,
        decimal rateToBase,
        string source = "Manual",
        CancellationToken ct = default)
    {
        await RequireManagementAccessAsync(userId, organisationId);
        var organisation = await db.Organisations.SingleAsync(x => x.Id == organisationId, ct);
        var from = NormalizeCode(fromCurrency);
        var to = NormalizeCode(organisation.BaseCurrency);
        if (from == to)
        {
            throw new InvalidOperationException("The base currency always has an exchange rate of 1.");
        }
        if (rateToBase <= 0)
        {
            throw new InvalidOperationException("Enter an exchange rate greater than zero.");
        }
        await RequireEnabledAsync(userId, organisationId, from, ct);

        var rate = await db.TransactionExchangeRates.SingleOrDefaultAsync(x =>
            x.OrganisationId == organisationId &&
            x.FromCurrency == from &&
            x.ToCurrency == to &&
            x.EffectiveDate == effectiveDate,
            ct);
        if (rate is null)
        {
            rate = new TransactionExchangeRate
            {
                OrganisationId = organisationId,
                FromCurrency = from,
                ToCurrency = to,
                EffectiveDate = effectiveDate,
                Rate = rateToBase,
                Source = source.Trim(),
                CreatedByUserId = userId
            };
            db.TransactionExchangeRates.Add(rate);
        }
        else
        {
            rate.Rate = rateToBase;
            rate.Source = source.Trim();
            rate.CreatedAt = DateTimeOffset.UtcNow;
            rate.CreatedByUserId = userId;
        }

        await db.SaveChangesAsync(ct);
        return rate;
    }

    public async Task<decimal?> FindRateAsync(
        string userId,
        Guid organisationId,
        string currency,
        DateOnly effectiveDate,
        CancellationToken ct = default)
    {
        await RequireOrganisationAccessAsync(userId, organisationId, ct);
        return await FindRateForOrganisationAsync(organisationId, currency, effectiveDate, ct);
    }

    public async Task<decimal?> FindRateForOrganisationAsync(
        Guid organisationId,
        string currency,
        DateOnly effectiveDate,
        CancellationToken ct = default)
    {
        var organisation = await db.Organisations.AsNoTracking().SingleAsync(x => x.Id == organisationId, ct);
        var normalized = NormalizeCode(currency);
        if (string.Equals(normalized, organisation.BaseCurrency, StringComparison.OrdinalIgnoreCase)) return 1m;
        return await db.TransactionExchangeRates.AsNoTracking()
            .Where(x => x.OrganisationId == organisationId &&
                x.FromCurrency == normalized &&
                x.ToCurrency == organisation.BaseCurrency &&
                x.EffectiveDate <= effectiveDate)
            .OrderByDescending(x => x.EffectiveDate)
            .Select(x => (decimal?)x.Rate)
            .FirstOrDefaultAsync(ct);
    }

    public async Task RequireEnabledAsync(
        string userId,
        Guid organisationId,
        string currency,
        CancellationToken ct = default)
    {
        await RequireOrganisationAccessAsync(userId, organisationId, ct);
        await RequireEnabledForOrganisationAsync(organisationId, currency, ct);
    }

    public async Task RequireEnabledForOrganisationAsync(
        Guid organisationId,
        string currency,
        CancellationToken ct = default)
    {
        var organisation = await db.Organisations.AsNoTracking().SingleAsync(x => x.Id == organisationId, ct);
        var normalized = NormalizeCode(currency);
        if (string.Equals(normalized, organisation.BaseCurrency, StringComparison.OrdinalIgnoreCase)) return;
        var configured = await db.OrganisationCurrencies.AsNoTracking().SingleOrDefaultAsync(
            x => x.OrganisationId == organisationId && x.Code == normalized,
            ct);
        var enabled = configured?.IsActive ?? StandardCurrencies.ContainsKey(normalized);
        if (!enabled)
        {
            throw new InvalidOperationException($"Currency {normalized} is not enabled for this organisation.");
        }
    }

    public static string NormalizeCode(string code)
    {
        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || normalized.Any(x => !char.IsAsciiLetter(x)))
        {
            throw new InvalidOperationException("Currency codes must contain exactly three letters.");
        }
        return normalized;
    }

    private async Task<Organisation> RequireOrganisationAccessAsync(
        string userId,
        Guid organisationId,
        CancellationToken ct)
    {
        if (await access.FindAsync(userId, organisationId) is null)
        {
            throw new UnauthorizedAccessException("You cannot access currencies for this organisation.");
        }
        return await db.Organisations.AsNoTracking().SingleAsync(x => x.Id == organisationId, ct);
    }

    private async Task RequireManagementAccessAsync(string userId, Guid organisationId)
    {
        if (!await access.CanManageTeamAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException("Only an owner or administrator can manage currencies.");
        }
    }
}
