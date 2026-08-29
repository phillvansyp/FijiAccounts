using FijiAccounts.Web.Services;
using Microsoft.Extensions.Options;

namespace FijiAccounts.Web.Tests;

public sealed class ImmutableDocumentStorageReadinessTests
{
    [Fact]
    public async Task DatabaseProvider_IsDevelopmentCompatibleButNotProductionReady()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new ImmutableDocumentStorageReadinessService(
            Options.Create(RequiredOptions()),
            new DatabaseImmutableDocumentStore(test.Db));

        var report = await service.CheckAsync();

        Assert.Equal(DatabaseImmutableDocumentStore.ProviderName, report.ActiveProvider);
        Assert.True(report.IsAvailable);
        Assert.True(report.IsDevelopmentCompatible);
        Assert.False(report.IsProductionReady);
        Assert.Equal(5, report.Controls.Count(x => x.Passed));
        Assert.Contains(report.Controls, x =>
            x.Name == "Native retention lock" && !x.Passed);
        Assert.Contains(report.Controls, x =>
            x.Name == "Retention period (7 years)" && !x.Passed);
    }

    [Fact]
    public async Task ConformingExternalProvider_IsProductionReady()
    {
        var service = new ImmutableDocumentStorageReadinessService(
            Options.Create(new ImmutableDocumentStorageOptions
            {
                Provider = "test-worm",
                RequireNativeRetentionLock = true,
                RequiredRetentionYears = 7
            }),
            new StubDiagnostics(new(
                "test-worm",
                "Test write-once provider",
                true,
                "Provider health probe succeeded.",
                new(true, true, true, true, 7))));

        var report = await service.CheckAsync();

        Assert.True(report.IsDevelopmentCompatible);
        Assert.True(report.IsProductionReady);
        Assert.All(report.Controls, x => Assert.True(x.Passed, x.Detail));
    }

    [Fact]
    public async Task ProviderMismatchAndProbeFailure_AreReportedWithoutThrowing()
    {
        var mismatch = new ImmutableDocumentStorageReadinessService(
            Options.Create(RequiredOptions()),
            new StubDiagnostics(new(
                "unexpected",
                "Unexpected provider",
                true,
                "Available.",
                new(true, true, true, true, 10))));
        var failed = new ImmutableDocumentStorageReadinessService(
            Options.Create(RequiredOptions()),
            new ThrowingDiagnostics());

        var mismatchReport = await mismatch.CheckAsync();
        var failedReport = await failed.CheckAsync();

        Assert.False(mismatchReport.IsDevelopmentCompatible);
        Assert.False(mismatchReport.IsProductionReady);
        Assert.Contains(mismatchReport.Controls, x =>
            x.Name == "Configured provider" && !x.Passed);
        Assert.False(failedReport.IsAvailable);
        Assert.False(failedReport.IsDevelopmentCompatible);
        Assert.Contains(failedReport.Controls, x =>
            x.Name == "Provider availability" &&
            !x.Passed &&
            x.Detail.Contains("probe failed", StringComparison.OrdinalIgnoreCase));
    }

    private static ImmutableDocumentStorageOptions RequiredOptions() =>
        new()
        {
            Provider = DatabaseImmutableDocumentStore.ProviderName,
            RequireNativeRetentionLock = true,
            RequiredRetentionYears = 7
        };

    private sealed class StubDiagnostics(ImmutableDocumentProviderHealth health)
        : IImmutableDocumentProviderDiagnostics
    {
        public Task<ImmutableDocumentProviderHealth> ProbeAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(health);
    }

    private sealed class ThrowingDiagnostics : IImmutableDocumentProviderDiagnostics
    {
        public Task<ImmutableDocumentProviderHealth> ProbeAsync(
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Provider probe failed.");
    }
}
