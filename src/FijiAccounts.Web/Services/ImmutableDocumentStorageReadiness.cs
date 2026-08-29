using Microsoft.Extensions.Options;

namespace FijiAccounts.Web.Services;

public sealed class ImmutableDocumentStorageOptions
{
    public const string SectionName = "DocumentStorage";

    public string Provider { get; set; } = DatabaseImmutableDocumentStore.ProviderName;
    public bool RequireNativeRetentionLock { get; set; } = true;
    public int RequiredRetentionYears { get; set; } = 7;
}

public sealed record ImmutableDocumentProviderCapabilities(
    bool ApplicationAppendOnly,
    bool IntegrityVerification,
    bool TenantIsolation,
    bool NativeRetentionLock,
    int? ConfiguredRetentionYears);

public sealed record ImmutableDocumentProviderHealth(
    string Provider,
    string DisplayName,
    bool Available,
    string Detail,
    ImmutableDocumentProviderCapabilities Capabilities);

public interface IImmutableDocumentProviderDiagnostics
{
    Task<ImmutableDocumentProviderHealth> ProbeAsync(
        CancellationToken cancellationToken = default);
}

public sealed record ImmutableDocumentStorageControl(
    string Name,
    bool Passed,
    string Detail);

public sealed record ImmutableDocumentStorageReadinessReport(
    string ConfiguredProvider,
    string ActiveProvider,
    string DisplayName,
    bool IsAvailable,
    bool IsDevelopmentCompatible,
    bool IsProductionReady,
    IReadOnlyList<ImmutableDocumentStorageControl> Controls);

public sealed class ImmutableDocumentStorageReadinessService(
    IOptions<ImmutableDocumentStorageOptions> options,
    IImmutableDocumentProviderDiagnostics diagnostics)
{
    public async Task<ImmutableDocumentStorageReadinessReport> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        var configured = options.Value;
        ImmutableDocumentProviderHealth health;
        try
        {
            health = await diagnostics.ProbeAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            health = new(
                "unavailable",
                "Unavailable provider",
                false,
                ex.Message,
                new(false, false, false, false, null));
        }

        var controls = new List<ImmutableDocumentStorageControl>
        {
            new(
                "Provider availability",
                health.Available,
                health.Detail),
            new(
                "Configured provider",
                configured.Provider.Equals(health.Provider, StringComparison.OrdinalIgnoreCase),
                configured.Provider.Equals(health.Provider, StringComparison.OrdinalIgnoreCase)
                    ? $"Active provider matches '{configured.Provider}'."
                    : $"Configured '{configured.Provider}', active '{health.Provider}'."),
            new(
                "Tenant isolation",
                health.Capabilities.TenantIsolation,
                health.Capabilities.TenantIsolation
                    ? "Object reads and references are scoped to their organisation."
                    : "The provider has not demonstrated tenant isolation."),
            new(
                "Integrity verification",
                health.Capabilities.IntegrityVerification,
                health.Capabilities.IntegrityVerification
                    ? "SHA-256 and content length are verified on read."
                    : "Checksum verification is not available."),
            new(
                "Append-only enforcement",
                health.Capabilities.ApplicationAppendOnly,
                health.Capabilities.ApplicationAppendOnly
                    ? "The provider rejects application-level updates and deletion."
                    : "Append-only enforcement is not available."),
            new(
                "Native retention lock",
                !configured.RequireNativeRetentionLock || health.Capabilities.NativeRetentionLock,
                health.Capabilities.NativeRetentionLock
                    ? "The provider reports a native write-once retention lock."
                    : "No provider-native retention lock is configured."),
            new(
                $"Retention period ({configured.RequiredRetentionYears} years)",
                health.Capabilities.ConfiguredRetentionYears is int years &&
                years >= configured.RequiredRetentionYears,
                health.Capabilities.ConfiguredRetentionYears is int configuredYears
                    ? $"Provider retention is configured for {configuredYears} years."
                    : "No provider-native retention period is configured.")
        };
        var developmentCompatible = controls.Take(5).All(x => x.Passed);

        return new(
            configured.Provider,
            health.Provider,
            health.DisplayName,
            health.Available,
            developmentCompatible,
            controls.All(x => x.Passed),
            controls);
    }
}
