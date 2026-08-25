namespace FijiAccounts.Web.Api.Mobile.V1;

public sealed class MobileApiOptions
{
    public const string SectionName = "MobileApi";

    public string MinimumIosVersion { get; set; } = "1.0.0";

    public string MinimumAndroidVersion { get; set; } = "1.0.0";

    public int RateLimitPermitLimit { get; set; } = 120;

    public int RateLimitWindowSeconds { get; set; } = 60;
}
