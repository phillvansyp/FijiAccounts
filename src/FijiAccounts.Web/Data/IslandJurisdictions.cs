namespace FijiAccounts.Web.Data;

public sealed record IslandJurisdiction(
    string CountryCode,
    string CountryName,
    string CurrencyCode,
    string TimeZoneId,
    string TaxLabel,
    int FinancialYearEndMonth,
    int FinancialYearEndDay,
    bool TaxPackEnabled);

public static class IslandJurisdictions
{
    public static readonly IReadOnlyList<IslandJurisdiction> All =
    [
        new("FJ", "Fiji", "FJD", "Pacific/Fiji", "VAT", 12, 31, true),
        new("WS", "Samoa", "WST", "Pacific/Apia", "VAGST", 6, 30, false),
        new("TO", "Tonga", "TOP", "Pacific/Tongatapu", "Consumption tax", 6, 30, false),
        new("VU", "Vanuatu", "VUV", "Pacific/Efate", "VAT", 12, 31, false),
        new("SB", "Solomon Islands", "SBD", "Pacific/Guadalcanal", "Indirect tax", 12, 31, false),
        new("PG", "Papua New Guinea", "PGK", "Pacific/Port_Moresby", "GST", 12, 31, false),
        new("CK", "Cook Islands", "NZD", "Pacific/Rarotonga", "VAT", 12, 31, false),
        new("KI", "Kiribati", "AUD", "Pacific/Tarawa", "VAT", 12, 31, false)
    ];

    public static IslandJurisdiction Get(string countryCode) =>
        All.SingleOrDefault(x => x.CountryCode == countryCode) ?? All[0];
}
