namespace FijiAccounts.Web.Data;

public static class OrganisationTime
{
    public static DateTimeOffset Convert(DateTimeOffset value, Organisation organisation) =>
        TimeZoneInfo.ConvertTime(value, ResolveTimeZone(organisation));

    public static TimeZoneInfo ResolveTimeZone(Organisation organisation)
    {
        var jurisdiction = IslandJurisdictions.All.FirstOrDefault(item =>
            item.CountryCode.Equals(organisation.CountryCode, StringComparison.OrdinalIgnoreCase));
        var configuredTimeZoneId = jurisdiction?.TimeZoneId ?? organisation.TimeZoneId;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(configuredTimeZoneId);
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
