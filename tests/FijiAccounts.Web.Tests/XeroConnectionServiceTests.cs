using System.Net;
using System.Text;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FijiAccounts.Web.Tests;

public sealed class XeroConnectionServiceTests
{
    [Fact]
    public async Task OAuthConnectionSupportsTenantChoiceAndReadOnlyStagingImport()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.CountryCode = "NZ";
        await test.Db.SaveChangesAsync();
        var migrationService = new XeroMigrationService(
            test.Db, test.Access, new DatabaseImmutableDocumentStore(test.Db));
        await migrationService.SaveAsync(test.UserId, test.Organisation.Id, new(
            "JCR 2006 Limited", new DateOnly(2026, 11, 1), true,
            false, false, false, false, false, false, false, false, null));

        var firstTenant = Guid.NewGuid();
        var secondTenant = Guid.NewGuid();
        var handler = new XeroHandler(firstTenant, secondTenant);
        var options = Options.Create(new XeroApiOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://account.example/api/integrations/xero/callback"
        });
        var api = new XeroApiClient(new HttpClient(handler), options);
        var service = new XeroConnectionService(
            test.Db,
            test.Access,
            api,
            new DatabaseImmutableDocumentStore(test.Db),
            new EphemeralDataProtectionProvider(),
            options);

        var authorize = new Uri(await service.CreateAuthorizationUriAsync(test.UserId, test.Organisation.Id));
        var query = QueryHelpers.ParseQuery(authorize.Query);
        Assert.Equal("code", query["response_type"]);
        Assert.Contains("accounting.invoices.read", query["scope"].ToString());
        Assert.DoesNotContain("accounting.invoices ", query["scope"].ToString());

        var state = Assert.Single(query["state"]);
        var callbackOrganisation = await service.CompleteAuthorizationAsync(test.UserId, "code", state!);
        Assert.Equal(test.Organisation.Id, callbackOrganisation);
        var connected = await service.GetStatusAsync(test.UserId, test.Organisation.Id);
        Assert.True(connected.IsConnected);
        Assert.Null(connected.TenantId);
        Assert.Equal(2, connected.AvailableTenants.Count);

        await service.SelectTenantAsync(test.UserId, test.Organisation.Id, secondTenant);
        await migrationService.SaveAsync(test.UserId, test.Organisation.Id, new(
            "JCR 2006 Limited", new DateOnly(2026, 11, 1), true,
            true, true, true, true, true, true, true, true, null));
        var result = await service.SyncAsync(test.UserId, test.Organisation.Id);

        Assert.True(result.ImportedDatasets >= 20);
        Assert.Empty(result.Failures);
        Assert.Equal(result.ImportedDatasets, await test.Db.XeroApiSnapshots.CountAsync());
        Assert.All(await test.Db.XeroApiSnapshots.ToListAsync(), x => Assert.Equal(64, x.Sha256.Length));
        Assert.All(handler.TenantHeaders, value => Assert.Equal(secondTenant.ToString(), value));
        var migration = await test.Db.XeroMigrations.SingleAsync();
        Assert.False(migration.ManagementApproved);
        Assert.False(migration.TrialBalanceReconciled);
        Assert.True(await test.Db.AuditEvents.AnyAsync(x => x.EventType == "XeroConnected"));
        Assert.True(await test.Db.AuditEvents.AnyAsync(x => x.EventType == "XeroApiSnapshotImported"));
    }

    [Fact]
    public async Task ConnectionCannotStartUntilDeploymentCredentialsAreConfigured()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var options = Options.Create(new XeroApiOptions());
        var service = new XeroConnectionService(
            test.Db,
            test.Access,
            new XeroApiClient(new HttpClient(new XeroHandler(Guid.NewGuid(), Guid.NewGuid())), options),
            new DatabaseImmutableDocumentStore(test.Db),
            new EphemeralDataProtectionProvider(),
            options);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAuthorizationUriAsync(test.UserId, test.Organisation.Id));

        Assert.Contains("app credentials", error.Message);
    }

    private sealed class XeroHandler(Guid firstTenant, Guid secondTenant) : HttpMessageHandler
    {
        public List<string?> TenantHeaders { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body;
            if (request.RequestUri!.AbsoluteUri == "https://identity.xero.com/connect/token")
            {
                body = """
                    {"access_token":"access-token","refresh_token":"refresh-token","expires_in":1800,"scope":"offline_access accounting.settings.read accounting.invoices.read"}
                    """;
            }
            else if (request.RequestUri.AbsoluteUri == "https://api.xero.com/connections")
            {
                body = $$"""
                    [
                      {"id":"connection-one","tenantId":"{{firstTenant}}","tenantName":"First Ltd","tenantType":"ORGANISATION"},
                      {"id":"connection-two","tenantId":"{{secondTenant}}","tenantName":"JCR 2006 Limited","tenantType":"ORGANISATION"}
                    ]
                    """;
            }
            else
            {
                TenantHeaders.Add(request.Headers.TryGetValues("xero-tenant-id", out var values) ? values.Single() : null);
                body = "{}";
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
