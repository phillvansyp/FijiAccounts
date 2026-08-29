using System.Net;
using System.Net.Http.Json;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace FijiAccounts.Web.Tests;

public sealed class OperationalReadinessTests
{
    [Fact]
    public async Task Database_verification_accepts_an_integral_SQLite_database()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();

        await AccountMaintenanceCommand.VerifyDatabaseAsync(
            test.Db,
            requireCurrentMigrations: false);
    }

    [Fact]
    public async Task Database_verification_detects_a_schema_without_migration_history()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AccountMaintenanceCommand.VerifyDatabaseAsync(
                test.Db,
                requireCurrentMigrations: true));

        Assert.Contains("pending migration", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Health_endpoints_distinguish_liveness_and_database_readiness()
    {
        await using var factory = new OperationalReadinessFactory();
        using var client = factory.CreateClient();

        var live = await client.GetAsync("/health/live");
        var ready = await client.GetAsync("/health/ready");
        var compatible = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, compatible.StatusCode);
        Assert.Equal("healthy", (await live.Content.ReadFromJsonAsync<HealthResponse>())!.Status);
        Assert.Equal("healthy", (await ready.Content.ReadFromJsonAsync<HealthResponse>())!.Status);
        Assert.Contains("no-store", live.Headers.CacheControl!.ToString());
        Assert.Contains("no-store", ready.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Operations_alert_uses_configured_recipient_and_redacted_message()
    {
        var delivery = new CapturingEmailDelivery();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Maintenance:OperationsAlert:Recipient"] = "owner@example.com",
                ["Maintenance:OperationsAlert:Subject"] = "Deployment failed",
                ["Maintenance:OperationsAlert:Body"] = "Review the protected workflow logs."
            })
            .Build();

        await AccountMaintenanceCommand.SendOperationsAlertAsync(
            delivery,
            configuration);

        Assert.NotNull(delivery.Message);
        Assert.Equal("owner@example.com", delivery.Message.Recipient);
        Assert.Equal("Deployment failed", delivery.Message.Subject);
        Assert.Equal("Review the protected workflow logs.", delivery.Message.TextBody);
    }

    private sealed record HealthResponse(string Status);

    private sealed class CapturingEmailDelivery : IEmailDeliveryService
    {
        public bool IsConfigured => true;
        public TransactionalEmail? Message { get; private set; }

        public Task SendAsync(
            TransactionalEmail email,
            CancellationToken cancellationToken = default)
        {
            Message = email;
            return Task.CompletedTask;
        }
    }

    private sealed class OperationalReadinessFactory : WebApplicationFactory<Program>
    {
        private readonly string databasePath = Path.Combine(
            Path.GetTempPath(),
            $"fiji-accounts-readiness-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var connectionString = $"Data Source={databasePath};Cache=Shared;Pooling=False";
            builder.UseEnvironment("Testing")
                .UseSetting("ConnectionStrings:DefaultConnection", connectionString)
                .UseSetting("Database:MigrateOnStartup", "true");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = connectionString,
                    ["Database:MigrateOnStartup"] = "true"
                }));
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            File.Delete(databasePath);
        }
    }
}
