namespace FijiAccounts.Web.Data;

public static class OrganisationTime
{
    public static DateTimeOffset Convert(DateTimeOffset value, Organisation organisation) =>
        TimeZoneInfo.ConvertTime(value, ResolveTimeZone(organisation));

    public static TimeZoneInfo ResolveTimeZone(Organisation organisation)
    {
        var configuredTimeZoneId = organisation.TimeZoneId;
        if (string.IsNullOrWhiteSpace(configuredTimeZoneId))
        {
            configuredTimeZoneId = IslandJurisdictions.Get(organisation.CountryCode).TimeZoneId;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(configuredTimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return ResolveJurisdictionFallback(organisation.CountryCode);
        }
        catch (InvalidTimeZoneException)
        {
            return ResolveJurisdictionFallback(organisation.CountryCode);
        }
    }

    private static TimeZoneInfo ResolveJurisdictionFallback(string countryCode)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(IslandJurisdictions.Get(countryCode).TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
