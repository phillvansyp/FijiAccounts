using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Tests;

public sealed class OrganisationTimeTests
{
    [Fact]
    public void Convert_UsesNewZealandDaylightSavingTime()
    {
        var organisation = CreateOrganisation("NZ", "Pacific/Auckland");
        var instant = new DateTimeOffset(2026, 10, 20, 22, 11, 0, TimeSpan.Zero);

        var local = OrganisationTime.Convert(instant, organisation);

        Assert.Equal(new DateTime(2026, 10, 21, 11, 11, 0), local.DateTime);
        Assert.Equal(TimeSpan.FromHours(13), local.Offset);
    }

    [Fact]
    public void Convert_UsesFijiTimeWithoutNewZealandDaylightSavingOffset()
    {
        var organisation = CreateOrganisation("FJ", "Pacific/Fiji");
        var instant = new DateTimeOffset(2026, 10, 20, 22, 11, 0, TimeSpan.Zero);

        var local = OrganisationTime.Convert(instant, organisation);

        Assert.Equal(new DateTime(2026, 10, 21, 10, 11, 0), local.DateTime);
        Assert.Equal(TimeSpan.FromHours(12), local.Offset);
    }

    [Fact]
    public void Convert_UsesJurisdictionWhenLegacyConfiguredTimeZoneIsWrong()
    {
        var organisation = CreateOrganisation("NZ", "Pacific/Fiji");
        var instant = new DateTimeOffset(2026, 10, 20, 22, 11, 0, TimeSpan.Zero);

        var local = OrganisationTime.Convert(instant, organisation);

        Assert.Equal(TimeSpan.FromHours(13), local.Offset);
        Assert.Equal(11, local.Hour);
    }

    private static Organisation CreateOrganisation(string countryCode, string timeZoneId) => new()
    {
        LegalName = "Time zone test",
        CountryCode = countryCode,
        TimeZoneId = timeZoneId
    };
}
