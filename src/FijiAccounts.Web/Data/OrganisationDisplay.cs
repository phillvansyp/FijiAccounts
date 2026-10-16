namespace FijiAccounts.Web.Data;

public static class OrganisationDisplay
{
    public static string CountryTag(string countryCode) =>
        countryCode.ToUpperInvariant() switch
        {
            "FJ" => "Fiji",
            "NZ" => "NZ",
            _ => countryCode.ToUpperInvariant()
        };

    public static string NameWithCountry(Organisation organisation) =>
        NameWithCountry(organisation.LegalName, organisation.CountryCode);

    public static string NameWithCountry(string legalName, string countryCode) =>
        $"{legalName} ({CountryTag(countryCode)})";

    public static string WorkspaceId(Organisation organisation) =>
        $"AI-{organisation.CountryCode.ToUpperInvariant()}-{organisation.Id:N}"[..14].ToUpperInvariant();

    public static string WorkspaceBadge(
        Organisation organisation,
        IEnumerable<Organisation> accessibleOrganisations)
    {
        var organisations = accessibleOrganisations.ToList();
        var standardBadge = Initials(organisation.LegalName);
        if (organisations.Count(item => Initials(item.LegalName) == standardBadge) == 1)
        {
            return standardBadge;
        }

        var countryBadge = $"{standardBadge[0]}{CountryInitial(organisation)}";
        var countryMatches = organisations
            .Where(item => Initials(item.LegalName) == standardBadge)
            .Where(item => $"{standardBadge[0]}{CountryInitial(item)}" == countryBadge)
            .OrderBy(item => item.Id)
            .ToList();

        if (countryMatches.Count == 1)
        {
            return countryBadge;
        }

        return $"{countryBadge}{countryMatches.FindIndex(item => item.Id == organisation.Id) + 1}";
    }

    private static string Initials(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 1
            ? string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])))
            : name[..Math.Min(2, name.Length)].ToUpperInvariant();
    }

    private static char CountryInitial(Organisation organisation) =>
        char.ToUpperInvariant(CountryTag(organisation.CountryCode)[0]);
}
