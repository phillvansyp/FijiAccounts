using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class ExternalImmutableDocumentProviderConformanceTests
{
    private static readonly Guid TestOrganisationId =
        Guid.Parse("a2ab24dc-d277-4ec3-a6ac-3ca352eadb39");

    [Fact]
    public async Task ConformingProvider_PassesEveryControl()
    {
        var provider = new FakeExternalProvider();

        var result = await ExternalImmutableDocumentProviderConformance.RunAsync(
            provider,
            TestOrganisationId,
            asAt: new DateOnly(2026, 8, 29));

        Assert.True(result.IsProductionReady);
        Assert.Equal("fake-worm", result.Provider);
        Assert.Equal(
            "conformance/account-island-worm-v1/a2ab24dcd2774ec3a6ac3ca352eadb39/2026",
            result.ProbeObjectKey);
        Assert.Equal(10, result.Controls.Count);
        Assert.All(result.Controls, control => Assert.True(control.Passed, control.Detail));
    }

    [Fact]
    public async Task UnsafeProviderBehaviour_IsDetectedByActiveControls()
    {
        var provider = new FakeExternalProvider
        {
            AllowDelete = true,
            AllowOverwrite = true,
            CorruptRead = true,
            LeakAcrossTenants = true
        };

        var result = await ExternalImmutableDocumentProviderConformance.RunAsync(
            provider,
            TestOrganisationId,
            asAt: new DateOnly(2026, 8, 29));

        Assert.False(result.IsProductionReady);
        AssertFailed(result, "Read-back integrity");
        AssertFailed(result, "Tenant isolation");
        AssertFailed(result, "Retention-locked deletion");
        AssertFailed(result, "Overwrite rejection");
    }

    [Fact]
    public async Task InsufficientProviderPolicy_IsNotProductionReady()
    {
        var provider = new FakeExternalProvider
        {
            NativeRetentionLock = false,
            ConfiguredRetentionYears = 3
        };

        var result = await ExternalImmutableDocumentProviderConformance.RunAsync(
            provider,
            TestOrganisationId,
            asAt: new DateOnly(2026, 8, 29));

        Assert.False(result.IsProductionReady);
        AssertFailed(result, "Native retention capability");
        AssertFailed(result, "Configured retention (7 years)");
        AssertFailed(result, "Write-once upload receipt");
    }

    [Fact]
    public async Task UnrelatedDeletionError_DoesNotProveRetentionLock()
    {
        var provider = new FakeExternalProvider
        {
            DeletionException = new TimeoutException("Provider timed out.")
        };

        var result = await ExternalImmutableDocumentProviderConformance.RunAsync(
            provider,
            TestOrganisationId,
            asAt: new DateOnly(2026, 8, 29));

        Assert.False(result.IsProductionReady);
        AssertFailed(result, "Retention-locked deletion");
    }

    [Fact]
    public async Task BlankProviderIdentity_IsNotProductionReady()
    {
        var result = await ExternalImmutableDocumentProviderConformance.RunAsync(
            new FakeExternalProvider { ProviderName = " " },
            TestOrganisationId,
            asAt: new DateOnly(2026, 8, 29));

        Assert.False(result.IsProductionReady);
        AssertFailed(result, "Provider identity");
    }

    [Fact]
    public async Task CallerCancellation_IsPropagated()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var provider = new FakeExternalProvider
        {
            ProbeException = new OperationCanceledException(cancellation.Token)
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ExternalImmutableDocumentProviderConformance.RunAsync(
                provider,
                TestOrganisationId,
                cancellationToken: cancellation.Token));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(101)]
    public async Task InvalidRetentionRequirement_IsRejected(int years)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            ExternalImmutableDocumentProviderConformance.RunAsync(
                new FakeExternalProvider(),
                TestOrganisationId,
                years));
    }

    [Fact]
    public async Task EmptyTestOrganisation_IsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            ExternalImmutableDocumentProviderConformance.RunAsync(
                new FakeExternalProvider(),
                Guid.Empty));
    }

    private static void AssertFailed(
        ExternalProviderConformanceResult result,
        string controlName) =>
        Assert.Contains(result.Controls, control =>
            control.Name == controlName && !control.Passed);

    private sealed class FakeExternalProvider : IExternalImmutableDocumentProvider
    {
        private readonly Dictionary<(Guid OrganisationId, string ObjectKey), StoredObject> _objects = [];

        public string ProviderName { get; init; } = "fake-worm";
        public bool AllowDelete { get; init; }
        public bool AllowOverwrite { get; init; }
        public bool CorruptRead { get; init; }
        public bool LeakAcrossTenants { get; init; }
        public bool NativeRetentionLock { get; init; } = true;
        public int ConfiguredRetentionYears { get; init; } = 7;
        public Exception? DeletionException { get; init; }
        public Exception? ProbeException { get; init; }

        public Task<ImmutableDocumentProviderHealth> ProbeAsync(
            CancellationToken cancellationToken = default)
        {
            if (ProbeException is not null)
            {
                throw ProbeException;
            }

            return Task.FromResult(new ImmutableDocumentProviderHealth(
                ProviderName,
                "Fake write-once provider",
                true,
                "Provider health probe succeeded.",
                new(true, true, true, NativeRetentionLock, ConfiguredRetentionYears)));
        }

        public Task<ExternalImmutableDocumentReceipt> WriteOnceAsync(
            ExternalImmutableDocumentWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            var key = (request.OrganisationId, request.ObjectKey);
            if (_objects.TryGetValue(key, out var existing))
            {
                if (existing.Sha256 == request.Sha256 &&
                    existing.Content.AsSpan().SequenceEqual(request.Content.Span))
                {
                    return Task.FromResult(CreateReceipt(request, existing.VersionId));
                }

                if (!AllowOverwrite)
                {
                    throw new ImmutableObjectAlreadyExistsException(
                        "The immutable object key already contains different content.");
                }
            }

            var versionId = $"version-{request.Sha256}";
            _objects[key] = new(
                request.Content.ToArray(),
                request.Sha256,
                request.RetainUntil,
                versionId);
            return Task.FromResult(CreateReceipt(request, versionId));
        }

        public Task<ExternalImmutableDocumentReadResult> ReadAsync(
            Guid organisationId,
            string objectKey,
            CancellationToken cancellationToken = default)
        {
            StoredObject? stored = null;
            if (!_objects.TryGetValue((organisationId, objectKey), out stored) && LeakAcrossTenants)
            {
                stored = _objects
                    .Where(pair => pair.Key.ObjectKey == objectKey)
                    .Select(pair => pair.Value)
                    .FirstOrDefault();
            }

            if (stored is null)
            {
                throw new FileNotFoundException("The immutable object was not found.");
            }

            var content = stored.Content.ToArray();
            if (CorruptRead && content.Length > 0)
            {
                content[0] ^= 0xff;
            }

            return Task.FromResult(new ExternalImmutableDocumentReadResult(
                ProviderName,
                objectKey,
                content,
                stored.Sha256,
                stored.RetainUntil,
                NativeRetentionLock,
                stored.VersionId));
        }

        public Task<bool> TryDeleteForConformanceAsync(
            Guid organisationId,
            string objectKey,
            CancellationToken cancellationToken = default)
        {
            if (DeletionException is not null)
            {
                throw DeletionException;
            }

            if (!AllowDelete)
            {
                throw new ImmutableObjectRetentionException(
                    "The provider retention lock prevents deletion.");
            }

            return Task.FromResult(_objects.Remove((organisationId, objectKey)));
        }

        private ExternalImmutableDocumentReceipt CreateReceipt(
            ExternalImmutableDocumentWriteRequest request,
            string versionId) =>
            new(
                ProviderName,
                request.ObjectKey,
                request.Sha256,
                request.Content.Length,
                request.RetainUntil,
                NativeRetentionLock,
                versionId);

        private sealed record StoredObject(
            byte[] Content,
            string Sha256,
            DateOnly RetainUntil,
            string VersionId);
    }
}
