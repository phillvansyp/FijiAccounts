using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace FijiAccounts.Web.Services;

public sealed record CurrencyOption(string Code, string Name, bool IsBaseCurrency, bool IsActive);

public sealed class TransactionCurrencyService(
    ApplicationDbContext db,
    TenantAccessService access,
    IHttpClientFactory? httpClientFactory = null)
{
    private const string RbfSource = "Reserve Bank of Fiji indicative daily rate";
    private static readonly Uri RbfRatesUri = new("https://www.rbf.gov.fj/");
    private static readonly Regex RbfDatePattern = new(
        @"Exchange Rates</strong></a></h2>\s*<p[^>]*>\s*<span[^>]*>(?<date>[^<]+)</span>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RbfRatePattern = new(
        @"<h4>\s*(?<currency>USD|AUD|NZD)\s*</h4>\s*<div class=[""']desc[""']>\s*(?<rate>[0-9]+(?:\.[0-9]+)?)\s*</div>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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
        var organisation = await RequireOrganisationAccessAsync(userId, organisationId, ct);
        var normalized = NormalizeCode(currency);
        if (string.Equals(normalized, organisation.BaseCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return 1m;
        }

        if (string.Equals(organisation.BaseCurrency, "FJD", StringComparison.OrdinalIgnoreCase) &&
            StandardCurrencies.ContainsKey(normalized))
        {
            var latest = await FindRateRecordAsync(organisationId, normalized, effectiveDate, ct);
            if (latest is null ||
                latest.Source == RbfSource && latest.EffectiveDate < effectiveDate)
            {
                try
                {
                    await RefreshRbfRatesIfNeededAsync(userId, organisationId, effectiveDate, ct);
                }
                catch (HttpRequestException)
                {
                    // Manual rates and the last cached official rate remain available offline.
                }
                catch (InvalidOperationException ex) when (
                    ex.Message.StartsWith("The Reserve Bank of Fiji", StringComparison.Ordinal))
                {
                    // A source-format problem must not stop a user entering a manual rate.
                }
            }
        }

        return await FindRateForOrganisationAsync(organisationId, currency, effectiveDate, ct);
    }

    public async Task<DateOnly?> RefreshOfficialRatesAsync(
        string userId,
        Guid organisationId,
        CancellationToken ct = default)
    {
        var organisation = await RequireOrganisationAccessAsync(userId, organisationId, ct);
        if (!string.Equals(organisation.BaseCurrency, "FJD", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Automatic Reserve Bank of Fiji rates are available only when FJD is the base currency.");
        }

        return await ImportRbfRatesAsync(userId, organisationId, ct);
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

    private async Task RefreshRbfRatesIfNeededAsync(
        string userId,
        Guid organisationId,
        DateOnly requestedDate,
        CancellationToken ct)
    {
        var recentBoundary = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7));
        if (requestedDate < recentBoundary)
        {
            return;
        }

        var latestImport = await db.TransactionExchangeRates.AsNoTracking()
            .Where(x => x.OrganisationId == organisationId &&
                x.ToCurrency == "FJD" &&
                x.Source == RbfSource)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => (DateTimeOffset?)x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (latestImport >= DateTimeOffset.UtcNow.AddHours(-6))
        {
            return;
        }

        await ImportRbfRatesAsync(userId, organisationId, ct);
    }

    private async Task<DateOnly> ImportRbfRatesAsync(
        string userId,
        Guid organisationId,
        CancellationToken ct)
    {
        if (httpClientFactory is null)
        {
            throw new InvalidOperationException("Automatic exchange-rate downloads are not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, RbfRatesUri);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 AccountIsland/1.0");
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(ct);
        var publication = ParseRbfRates(html);

        foreach (var quote in publication.Rates)
        {
            var existing = await db.TransactionExchangeRates.SingleOrDefaultAsync(x =>
                x.OrganisationId == organisationId &&
                x.FromCurrency == quote.Key &&
                x.ToCurrency == "FJD" &&
                x.EffectiveDate == publication.EffectiveDate,
                ct);

            // A user-entered or bank-supplied rate is the stronger accounting evidence.
            if (existing is not null && existing.Source != RbfSource)
            {
                continue;
            }

            var rateToBase = decimal.Round(1m / quote.Value, 8, MidpointRounding.AwayFromZero);
            if (existing is null)
            {
                db.TransactionExchangeRates.Add(new TransactionExchangeRate
                {
                    OrganisationId = organisationId,
                    FromCurrency = quote.Key,
                    ToCurrency = "FJD",
                    EffectiveDate = publication.EffectiveDate,
                    Rate = rateToBase,
                    Source = RbfSource,
                    CreatedByUserId = userId
                });
            }
            else
            {
                existing.Rate = rateToBase;
                existing.CreatedAt = DateTimeOffset.UtcNow;
                existing.CreatedByUserId = userId;
            }
        }

        await db.SaveChangesAsync(ct);
        return publication.EffectiveDate;
    }

    private Task<TransactionExchangeRate?> FindRateRecordAsync(
        Guid organisationId,
        string currency,
        DateOnly effectiveDate,
        CancellationToken ct) =>
        db.TransactionExchangeRates.AsNoTracking()
            .Where(x => x.OrganisationId == organisationId &&
                x.FromCurrency == currency &&
                x.ToCurrency == "FJD" &&
                x.EffectiveDate <= effectiveDate)
            .OrderByDescending(x => x.EffectiveDate)
            .FirstOrDefaultAsync(ct);

    internal static RbfRatePublication ParseRbfRates(string html)
    {
        var dateText = WebUtility.HtmlDecode(
            RbfDatePattern.Match(html).Groups["date"].Value).Trim();
        if (!DateOnly.TryParseExact(
                dateText,
                "d MMMM yyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var effectiveDate))
        {
            throw new InvalidOperationException(
                "The Reserve Bank of Fiji exchange-rate date could not be read.");
        }

        var rates = RbfRatePattern.Matches(html)
            .Select(match => new
            {
                Currency = match.Groups["currency"].Value.ToUpperInvariant(),
                Value = decimal.TryParse(
                    match.Groups["rate"].Value,
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var value)
                    ? value
                    : 0m
            })
            .Where(x => x.Value > 0)
            .ToDictionary(x => x.Currency, x => x.Value, StringComparer.OrdinalIgnoreCase);
        if (rates.Count != 3)
        {
            throw new InvalidOperationException(
                "The Reserve Bank of Fiji USD, AUD and NZD rates could not be read.");
        }

        return new RbfRatePublication(effectiveDate, rates);
    }
}

internal sealed record RbfRatePublication(
    DateOnly EffectiveDate,
    IReadOnlyDictionary<string, decimal> Rates);
