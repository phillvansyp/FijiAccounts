using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record GroupExchangeRateConfiguration(
    Guid GroupId,
    string GroupName,
    string PresentationCurrency,
    bool CanManage,
    IReadOnlyList<string> CompanyCurrencies,
    IReadOnlyList<GroupExchangeRate> Rates);

public sealed record SaveGroupExchangeRateRequest(
    Guid CurrentOrganisationId,
    string FromCurrency,
    GroupExchangeRateType Type,
    DateOnly EffectiveDate,
    decimal Rate);

public sealed class GroupExchangeRateService(ApplicationDbContext db)
{
    public async Task<GroupExchangeRateConfiguration> GetAsync(
        string userId,
        Guid currentOrganisationId,
        CancellationToken cancellationToken = default)
    {
        var group = await RequireGroupAsync(userId, currentOrganisationId, false, cancellationToken);
        var rates = await db.GroupExchangeRates
            .AsNoTracking()
            .Where(x => x.OrganisationGroupId == group.Id)
            .OrderBy(x => x.FromCurrency)
            .ThenBy(x => x.Type)
            .ThenByDescending(x => x.EffectiveDate)
            .ToListAsync(cancellationToken);

        return new(
            group.Id,
            group.Name,
            group.PresentationCurrency,
            group.CanManage,
            group.CompanyCurrencies,
            rates);
    }

    public async Task SetPresentationCurrencyAsync(
        string userId,
        Guid currentOrganisationId,
        string presentationCurrency,
        CancellationToken cancellationToken = default)
    {
        var group = await RequireGroupAsync(userId, currentOrganisationId, true, cancellationToken);
        var currency = NormaliseCurrency(presentationCurrency);
        if (!IslandJurisdictions.All.Any(x => x.CurrencyCode == currency))
        {
            throw new InvalidOperationException("Select a supported presentation currency.");
        }

        await db.OrganisationGroups
            .Where(x => x.Id == group.Id)
            .ExecuteUpdateAsync(
                update => update.SetProperty(x => x.PresentationCurrency, currency),
                cancellationToken);
    }

    public async Task SaveAsync(
        string userId,
        SaveGroupExchangeRateRequest request,
        CancellationToken cancellationToken = default)
    {
        var group = await RequireGroupAsync(userId, request.CurrentOrganisationId, true, cancellationToken);
        var fromCurrency = NormaliseCurrency(request.FromCurrency);
        if (!group.CompanyCurrencies.Contains(fromCurrency, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The source currency must belong to a company in this group.");
        }

        if (fromCurrency == group.PresentationCurrency)
        {
            throw new InvalidOperationException("A rate is not required for the presentation currency.");
        }

        if (request.Rate <= 0m)
        {
            throw new InvalidOperationException("The exchange rate must be greater than zero.");
        }

        var existing = await db.GroupExchangeRates.SingleOrDefaultAsync(
            x => x.OrganisationGroupId == group.Id &&
                 x.FromCurrency == fromCurrency &&
                 x.ToCurrency == group.PresentationCurrency &&
                 x.Type == request.Type &&
                 x.EffectiveDate == request.EffectiveDate,
            cancellationToken);
        if (existing is null)
        {
            db.GroupExchangeRates.Add(new GroupExchangeRate
            {
                OrganisationGroupId = group.Id,
                FromCurrency = fromCurrency,
                ToCurrency = group.PresentationCurrency,
                Type = request.Type,
                EffectiveDate = request.EffectiveDate,
                Rate = request.Rate
            });
        }
        else
        {
            existing.Rate = request.Rate;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<GroupAccess> RequireGroupAsync(
        string userId,
        Guid currentOrganisationId,
        bool requireManager,
        CancellationToken cancellationToken)
    {
        var storedGroup = await db.OrganisationGroups
            .AsNoTracking()
            .Include(x => x.Companies)
            .Where(x => x.Companies.Any(company => company.Id == currentOrganisationId))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("This organisation does not belong to an organisation group.");
        var group = new GroupAccess(
            storedGroup.Id,
            storedGroup.Name,
            storedGroup.PresentationCurrency,
            storedGroup.Companies.Select(x => x.Id).ToList(),
            storedGroup.Companies.Select(x => x.BaseCurrency).Distinct().ToList());
        var groupRole = await db.OrganisationGroupMemberships
            .AsNoTracking()
            .Where(x => x.OrganisationGroupId == group.Id && x.UserId == userId)
            .Select(x => (OrganisationGroupRole?)x.Role)
            .SingleOrDefaultAsync(cancellationToken);
        if (groupRole is not null)
        {
            if (requireManager && groupRole == OrganisationGroupRole.Viewer)
            {
                throw new UnauthorizedAccessException("You do not have permission to manage group exchange rates.");
            }

            return group with { CanManage = groupRole != OrganisationGroupRole.Viewer };
        }

        var managedCompanyIds = await db.OrganisationMemberships
            .AsNoTracking()
            .Where(x =>
                x.UserId == userId &&
                x.Organisation.OrganisationGroupId == group.Id &&
                (x.Role == OrganisationRole.Owner || x.Role == OrganisationRole.Administrator))
            .Select(x => x.OrganisationId)
            .ToListAsync(cancellationToken);
        if (group.CompanyIds.Any(id => !managedCompanyIds.Contains(id)))
        {
            throw new UnauthorizedAccessException(
                requireManager
                    ? "You do not have permission to manage group exchange rates."
                    : "You do not have access to this organisation group.");
        }

        return group with { CanManage = true };
    }

    private static string NormaliseCurrency(string value)
    {
        var currency = value.Trim().ToUpperInvariant();
        if (currency.Length != 3)
        {
            throw new InvalidOperationException("Enter a three-letter currency code.");
        }

        return currency;
    }

    private sealed record GroupAccess(
        Guid Id,
        string Name,
        string PresentationCurrency,
        IReadOnlyList<Guid> CompanyIds,
        IReadOnlyList<string> CompanyCurrencies,
        bool CanManage = false);
}
