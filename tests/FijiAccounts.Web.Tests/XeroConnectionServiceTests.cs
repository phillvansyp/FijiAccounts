using System.IO.Compression;
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
        Assert.Null((await test.Db.XeroConnections.SingleAsync()).SyncLeaseUntil);
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

    [Fact]
    public async Task SyncRetainsEveryReportedAttachmentInADeterministicEvidencePackage()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.CountryCode = "NZ";
        await test.Db.SaveChangesAsync();
        var migrationService = new XeroMigrationService(
            test.Db, test.Access, new DatabaseImmutableDocumentStore(test.Db));
        await migrationService.SaveAsync(test.UserId, test.Organisation.Id, new(
            "BOSS Technologies Limited", new DateOnly(2026, 10, 18), true,
            false, false, false, false, false, false, false, false, null));

        var tenantId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var handler = new AttachmentXeroHandler(tenantId, invoiceId, attachmentId);
        var options = Options.Create(new XeroApiOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://account.example/api/integrations/xero/callback"
        });
        var store = new DatabaseImmutableDocumentStore(test.Db);
        var service = new XeroConnectionService(
            test.Db,
            test.Access,
            new XeroApiClient(new HttpClient(handler), options),
            store,
            new EphemeralDataProtectionProvider(),
            options);

        var authorize = new Uri(await service.CreateAuthorizationUriAsync(test.UserId, test.Organisation.Id));
        var state = Assert.Single(QueryHelpers.ParseQuery(authorize.Query)["state"]);
        await service.CompleteAuthorizationAsync(test.UserId, "code", state!);

        var first = await service.SyncAsync(test.UserId, test.Organisation.Id);
        Assert.Empty(first.Failures);
        var retained = Assert.Single(await test.Db.XeroMigrationFiles
            .Where(x => x.Category == "Document attachments")
            .ToListAsync());
        var zipBytes = await store.ReadVerifiedAsync(test.Organisation.Id, retained.ImmutableDocumentObjectId);
        using (var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read))
        {
            Assert.Equal(2, archive.Entries.Count);
            Assert.Contains(archive.Entries, x => x.FullName == "manifest.json");
            Assert.Contains(archive.Entries, x => x.FullName.Contains(attachmentId.ToString("D"), StringComparison.Ordinal));
        }
        Assert.DoesNotContain(handler.Requests, x => x.Contains("AgedReceivablesByContact", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.Requests, x => x.Contains("AgedPayablesByContact", StringComparison.Ordinal));
        Assert.Contains("application/pdf", handler.BinaryContentTypes);

        var second = await service.SyncAsync(test.UserId, test.Organisation.Id);
        Assert.Empty(second.Failures);
        Assert.Equal(1, await test.Db.XeroMigrationFiles.CountAsync(x => x.Category == "Document attachments"));
    }

    [Fact]
    public async Task TimedOutDatasetIsReportedWithoutDiscardingCompletedSourceCopy()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.CountryCode = "NZ";
        await test.Db.SaveChangesAsync();
        var migrationService = new XeroMigrationService(
            test.Db, test.Access, new DatabaseImmutableDocumentStore(test.Db));
        await migrationService.SaveAsync(test.UserId, test.Organisation.Id, new(
            "JCR 2006 Limited", new DateOnly(2026, 11, 1), true,
            false, false, false, false, false, false, false, false, null));

        var tenantId = Guid.NewGuid();
        var handler = new TimeoutXeroHandler(tenantId);
        var options = Options.Create(new XeroApiOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://account.example/api/integrations/xero/callback"
        });
        var service = new XeroConnectionService(
            test.Db,
            test.Access,
            new XeroApiClient(new HttpClient(handler), options),
            new DatabaseImmutableDocumentStore(test.Db),
            new EphemeralDataProtectionProvider(),
            options);

        var authorize = new Uri(await service.CreateAuthorizationUriAsync(test.UserId, test.Organisation.Id));
        var state = Assert.Single(QueryHelpers.ParseQuery(authorize.Query)["state"]);
        await service.CompleteAuthorizationAsync(test.UserId, "code", state!);
        await service.SelectTenantAsync(test.UserId, test.Organisation.Id, tenantId);

        var result = await service.SyncAsync(test.UserId, test.Organisation.Id);

        Assert.True(result.ImportedDatasets > 0);
        var failure = Assert.Single(result.Failures, x => x.Dataset == "Budgets");
        Assert.Contains("timed out", failure.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.True(await test.Db.XeroApiSnapshots.AnyAsync());
        var connection = await test.Db.XeroConnections.SingleAsync();
        Assert.Contains("Budgets", connection.LastSyncError);
        Assert.Null(connection.SyncLeaseUntil);
        var preparation = new XeroImportPreparationService(
            test.Db, test.Access, new DatabaseImmutableDocumentStore(test.Db));
        var assistant = await new XeroMigrationAssistantService(test.Db, test.Access, preparation)
            .GetAsync(test.UserId, test.Organisation.Id);
        Assert.True(Assert.Single(assistant.Checks, x => x.Key == "source").Passed);
        Assert.NotEqual(XeroMigrationAssistantAction.CopySource, assistant.NextAction);
    }

    [Fact]
    public async Task ResponseBodyThatStopsArrivingIsTimedOut()
    {
        var options = Options.Create(new XeroApiOptions { RequestTimeoutSeconds = 1 });
        var api = new XeroApiClient(new HttpClient(new StalledResponseHandler()), options);

        var error = await Assert.ThrowsAsync<XeroApiException>(() =>
            api.GetJsonAsync("access-token", Guid.NewGuid(), "Accounts"));

        Assert.Equal(HttpStatusCode.RequestTimeout, error.StatusCode);
        Assert.Contains("timed out", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RotatedRefreshTokenIsPersistedImmediately()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var protection = new EphemeralDataProtectionProvider();
        var protector = protection.CreateProtector("AccountIsland.Xero.Token.v1");
        var connection = new XeroConnection
        {
            OrganisationId = test.Organisation.Id,
            ProtectedAccessToken = protector.Protect("expired-access"),
            ProtectedRefreshToken = protector.Protect("old-refresh"),
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            GrantedScopes = "offline_access accounting.settings.read",
            ConnectedByUserId = test.UserId,
            UpdatedByUserId = test.UserId
        };
        test.Db.XeroConnections.Add(connection);
        await test.Db.SaveChangesAsync();
        var options = Options.Create(new XeroApiOptions());
        var service = new XeroConnectionService(
            test.Db,
            test.Access,
            new XeroApiClient(new HttpClient(new RotatingTokenHandler()), options),
            new DatabaseImmutableDocumentStore(test.Db),
            protection,
            options);

        var accessToken = await service.GetAccessTokenAsync(connection, test.UserId, CancellationToken.None);
        test.Db.ChangeTracker.Clear();
        var stored = await test.Db.XeroConnections.SingleAsync();

        Assert.Equal("new-access", accessToken);
        Assert.Equal("new-refresh", protector.Unprotect(stored.ProtectedRefreshToken));
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

    private sealed class AttachmentXeroHandler(Guid tenantId, Guid invoiceId, Guid attachmentId) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public List<string?> BinaryContentTypes { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            if (request.RequestUri.AbsoluteUri == "https://identity.xero.com/connect/token")
            {
                return Json("""
                    {"access_token":"access-token","refresh_token":"refresh-token","expires_in":1800,"scope":"offline_access accounting.attachments.read"}
                    """);
            }
            if (request.RequestUri.AbsoluteUri == "https://api.xero.com/connections")
            {
                return Json($$"""
                    [{"id":"connection-one","tenantId":"{{tenantId}}","tenantName":"BOSS Technologies Limited","tenantType":"ORGANISATION"}]
                    """);
            }

            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith($"/Invoices/{invoiceId:D}/Attachments/{attachmentId:D}", StringComparison.Ordinal))
            {
                BinaryContentTypes.Add(request.Headers.TryGetValues("contentType", out var values)
                    ? values.Single()
                    : null);
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes("attachment-content"))
                };
                response.Content.Headers.ContentType = new("application/pdf");
                return Task.FromResult(response);
            }
            if (path.EndsWith($"/Invoices/{invoiceId:D}/Attachments", StringComparison.Ordinal))
            {
                return Json($$"""
                    {"Attachments":[{"AttachmentID":"{{attachmentId}}","FileName":"invoice.pdf","MimeType":"application/pdf","ContentLength":18}]}
                    """);
            }
            if (path.EndsWith("/Invoices", StringComparison.Ordinal) &&
                request.RequestUri.Query.Contains("ACCREC", StringComparison.Ordinal))
            {
                return Json($$"""
                    {"Invoices":[{"InvoiceID":"{{invoiceId}}","HasAttachments":true}]}
                    """);
            }
            if (path.EndsWith("/Invoices", StringComparison.Ordinal)) return Json("{\"Invoices\":[]}");
            return Json("{}");
        }

        private static Task<HttpResponseMessage> Json(string body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
    }

    private sealed class TimeoutXeroHandler(Guid tenantId) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri == "https://identity.xero.com/connect/token")
                return Json("""{"access_token":"access-token","refresh_token":"refresh-token","expires_in":1800,"scope":"offline_access accounting.settings.read accounting.budgets.read"}""");
            if (request.RequestUri.AbsoluteUri == "https://api.xero.com/connections")
                return Json($$"""[{"id":"connection-one","tenantId":"{{tenantId}}","tenantName":"JCR 2006 Limited","tenantType":"ORGANISATION"}]""");
            if (request.RequestUri.AbsolutePath.EndsWith("/Budgets", StringComparison.Ordinal))
                throw new TaskCanceledException("Simulated Xero timeout.");
            return Json("{}");
        }

        private static Task<HttpResponseMessage> Json(string body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
    }

    private sealed class StalledResponseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StalledContent()
            });
    }

    private sealed class RotatingTokenHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"access_token":"new-access","refresh_token":"new-refresh","expires_in":1800,"scope":"offline_access accounting.settings.read"}""",
                    Encoding.UTF8,
                    "application/json")
            });
    }

    private sealed class StalledContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.Delay(Timeout.InfiniteTimeSpan);

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
