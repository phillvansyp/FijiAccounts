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
}
