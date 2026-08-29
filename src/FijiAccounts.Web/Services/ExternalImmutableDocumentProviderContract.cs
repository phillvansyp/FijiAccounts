using System.Security.Cryptography;
using System.Text;

namespace FijiAccounts.Web.Services;

public sealed record ExternalImmutableDocumentWriteRequest(
    Guid OrganisationId,
    string ObjectKey,
    ReadOnlyMemory<byte> Content,
    string Sha256,
    DateOnly RetainUntil);

public sealed record ExternalImmutableDocumentReceipt(
    string Provider,
    string ObjectKey,
    string Sha256,
    long ContentLength,
    DateOnly RetainUntil,
    bool NativeRetentionLock,
    string? ProviderVersionId = null);

public sealed record ExternalImmutableDocumentReadResult(
    string Provider,
    string ObjectKey,
    byte[] Content,
    string Sha256,
    DateOnly RetainUntil,
    bool NativeRetentionLock,
    string? ProviderVersionId = null);

public sealed class ImmutableObjectAlreadyExistsException(string message)
    : InvalidOperationException(message);

public sealed class ImmutableObjectRetentionException(string message)
    : InvalidOperationException(message);

public interface IExternalImmutableDocumentProvider
    : IImmutableDocumentProviderDiagnostics
{
    string ProviderName { get; }

    Task<ExternalImmutableDocumentReceipt> WriteOnceAsync(
        ExternalImmutableDocumentWriteRequest request,
        CancellationToken cancellationToken = default);

    Task<ExternalImmutableDocumentReadResult> ReadAsync(
        Guid organisationId,
        string objectKey,
        CancellationToken cancellationToken = default);

    Task<bool> TryDeleteForConformanceAsync(
        Guid organisationId,
        string objectKey,
        CancellationToken cancellationToken = default);
}

public sealed record ExternalProviderConformanceControl(
    string Name,
    bool Passed,
    string Detail);

public sealed record ExternalProviderConformanceResult(
    string Provider,
    string ProbeObjectKey,
    DateTimeOffset CompletedAt,
    bool IsProductionReady,
    IReadOnlyList<ExternalProviderConformanceControl> Controls);

public static class ExternalImmutableDocumentProviderConformance
{
    public const string ContractVersion = "account-island-worm-v1";

    public static async Task<ExternalProviderConformanceResult> RunAsync(
        IExternalImmutableDocumentProvider provider,
        Guid testOrganisationId,
        int requiredRetentionYears = 7,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (testOrganisationId == Guid.Empty)
        {
            throw new ArgumentException(
                "A dedicated provider-conformance organisation is required.",
                nameof(testOrganisationId));
        }
        if (requiredRetentionYears is < 7 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requiredRetentionYears),
                "Retention must be between 7 and 100 years.");
        }

        var today = asAt ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var retainUntil = new DateOnly(
            checked(today.Year + requiredRetentionYears),
            12,
            31);
        var objectKey = $"conformance/{ContractVersion}/{testOrganisationId:N}/{today.Year}";
        var content = Encoding.UTF8.GetBytes(
            $"{ContractVersion}|{testOrganisationId:N}|{today.Year}");
        var sha256 = Convert.ToHexString(SHA256.HashData(content));
        var request = new ExternalImmutableDocumentWriteRequest(
            testOrganisationId,
            objectKey,
            content,
            sha256,
            retainUntil);
        var controls = new List<ExternalProviderConformanceControl>();

        ImmutableDocumentProviderHealth? health = null;
        try
        {
            health = await provider.ProbeAsync(cancellationToken);
            controls.Add(new(
                "Provider availability",
                health.Available,
                health.Detail));
            controls.Add(new(
                "Provider identity",
                !string.IsNullOrWhiteSpace(provider.ProviderName) &&
                provider.ProviderName.Equals(health.Provider, StringComparison.OrdinalIgnoreCase),
                $"Contract provider '{provider.ProviderName}', probe provider '{health.Provider}'."));
            controls.Add(new(
                "Native retention capability",
                health.Capabilities.NativeRetentionLock,
                health.Capabilities.NativeRetentionLock
                    ? "Provider reports native write-once retention support."
                    : "Provider does not report native write-once retention support."));
            controls.Add(new(
                $"Configured retention ({requiredRetentionYears} years)",
                health.Capabilities.ConfiguredRetentionYears is int years &&
                years >= requiredRetentionYears,
                health.Capabilities.ConfiguredRetentionYears is int configuredYears
                    ? $"Provider reports {configuredYears} configured years."
                    : "Provider did not report a configured retention period."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            controls.Add(new("Provider availability", false, ex.Message));
        }

        ExternalImmutableDocumentReceipt? receipt = null;
        try
        {
            receipt = await provider.WriteOnceAsync(request, cancellationToken);
            var valid = receipt.Provider.Equals(provider.ProviderName, StringComparison.OrdinalIgnoreCase) &&
                        receipt.ObjectKey == objectKey &&
                        receipt.Sha256.Equals(sha256, StringComparison.Ordinal) &&
                        receipt.ContentLength == content.LongLength &&
                        receipt.RetainUntil >= retainUntil &&
                        receipt.NativeRetentionLock;
            controls.Add(new(
                "Write-once upload receipt",
                valid,
                valid
                    ? "Receipt confirms key, checksum, length and native retention lock."
                    : "Upload receipt did not confirm every requested immutable property."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            controls.Add(new("Write-once upload receipt", false, ex.Message));
        }

        if (receipt is not null)
        {
            try
            {
                var read = await provider.ReadAsync(
                    testOrganisationId,
                    objectKey,
                    cancellationToken);
                var actualHash = Convert.ToHexString(SHA256.HashData(read.Content));
                var valid = read.Provider.Equals(provider.ProviderName, StringComparison.OrdinalIgnoreCase) &&
                            read.ObjectKey == objectKey &&
                            read.Content.AsSpan().SequenceEqual(content) &&
                            read.Sha256.Equals(sha256, StringComparison.Ordinal) &&
                            actualHash.Equals(sha256, StringComparison.Ordinal) &&
                            read.RetainUntil >= retainUntil &&
                            read.NativeRetentionLock;
                controls.Add(new(
                    "Read-back integrity",
                    valid,
                    valid
                        ? "Read-back bytes, SHA-256 and retention metadata match the upload."
                        : "Read-back content or immutable metadata did not match."));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                controls.Add(new("Read-back integrity", false, ex.Message));
            }

            try
            {
                var repeated = await provider.WriteOnceAsync(request, cancellationToken);
                var valid = repeated.ObjectKey == receipt.ObjectKey &&
                            repeated.Sha256.Equals(receipt.Sha256, StringComparison.Ordinal) &&
                            repeated.ProviderVersionId == receipt.ProviderVersionId;
                controls.Add(new(
                    "Idempotent retry",
                    valid,
                    valid
                        ? "Repeating the identical request returned the original immutable object."
                        : "An identical retry created or identified a different object version."));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                controls.Add(new("Idempotent retry", false, ex.Message));
            }

            try
            {
                _ = await provider.ReadAsync(
                    Guid.NewGuid(),
                    objectKey,
                    cancellationToken);
                controls.Add(new(
                    "Tenant isolation",
                    false,
                    "The conformance object was readable through another organisation."));
            }
            catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException)
            {
                controls.Add(new(
                    "Tenant isolation",
                    true,
                    "Cross-organisation read access was rejected."));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                controls.Add(new("Tenant isolation", false, ex.Message));
            }

            try
            {
                var deleted = await provider.TryDeleteForConformanceAsync(
                    testOrganisationId,
                    objectKey,
                    cancellationToken);
                controls.Add(new(
                    "Retention-locked deletion",
                    !deleted,
                    deleted
                        ? "Provider deleted an object inside its retention period."
                        : "Provider rejected deletion inside the retention period."));
            }
            catch (ImmutableObjectRetentionException ex)
            {
                controls.Add(new(
                    "Retention-locked deletion",
                    true,
                    $"Provider rejected deletion: {ex.Message}"));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                controls.Add(new("Retention-locked deletion", false, ex.Message));
            }

            var conflictingContent = Encoding.UTF8.GetBytes(
                $"{ContractVersion}|conflicting-content");
            var conflictingRequest = request with
            {
                Content = conflictingContent,
                Sha256 = Convert.ToHexString(SHA256.HashData(conflictingContent))
            };
            try
            {
                _ = await provider.WriteOnceAsync(conflictingRequest, cancellationToken);
                controls.Add(new(
                    "Overwrite rejection",
                    false,
                    "Provider accepted different content for an existing immutable key."));
            }
            catch (ImmutableObjectAlreadyExistsException)
            {
                controls.Add(new(
                    "Overwrite rejection",
                    true,
                    "Provider rejected different content for the existing key."));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                controls.Add(new("Overwrite rejection", false, ex.Message));
            }
        }

        return new(
            provider.ProviderName,
            objectKey,
            DateTimeOffset.UtcNow,
            controls.Count >= 10 && controls.All(x => x.Passed),
            controls);
    }
}
