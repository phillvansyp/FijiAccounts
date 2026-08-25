namespace FijiAccounts.Web.Authentication;

public sealed class MobileAuthenticationOptions
{
    public const string SectionName = "MobileAuthentication";

    public bool Enabled { get; set; }

    public string ClientId { get; set; } = "account-island-mobile";

    public string IosRedirectUri { get; set; } =
        "https://app.accountisland.com/mobile/callback/ios";

    public string AndroidRedirectUri { get; set; } =
        "https://app.accountisland.com/mobile/callback/android";

    public string? SigningCertificatePath { get; set; }

    public string? SigningCertificatePassword { get; set; }

    public string? EncryptionCertificatePath { get; set; }

    public string? EncryptionCertificatePassword { get; set; }
}
