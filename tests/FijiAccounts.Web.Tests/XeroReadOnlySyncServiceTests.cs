using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FijiAccounts.Web.Tests;

public sealed class XeroReadOnlySyncServiceTests
{
    [Fact]
    public async Task Sync_is_one_way_idempotent_and_refreshes_safe_master_data()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        test.Organisation.CountryCode = "NZ";
        test.Organisation.BaseCurrency = "NZD";
        await test.Db.SaveChangesAsync();

        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var handler = new ReadOnlyXeroHandler(tenantId, accountId, contactId, itemId);
        var options = Options.Create(new XeroApiOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://account.example/api/integrations/xero/callback",
            SyncIntervalMinutes = 15,
            FullReconciliationHours = 24
        });
        var api = new XeroApiClient(new HttpClient(handler), options);
        var provider = new EphemeralDataProtectionProvider();
        var connectionService = new XeroConnectionService(
            test.Db, test.Access, api, new DatabaseImmutableDocumentStore(test.Db), provider, options);
        var sync = new XeroReadOnlySyncService(
            test.Db, test.Access, api, connectionService, options,
            NullLogger<XeroReadOnlySyncService>.Instance);

        var authorize = new Uri(await connectionService.CreateAuthorizationUriAsync(test.UserId, test.Organisation.Id));
        var state = Assert.Single(QueryHelpers.ParseQuery(authorize.Query)["state"]);
        await connectionService.CompleteAuthorizationAsync(test.UserId, "code", state!);
        await connectionService.SelectTenantAsync(test.UserId, test.Organisation.Id, tenantId);
        await sync.SetEnabledAsync(test.UserId, test.Organisation.Id, true);

        var first = await sync.RunNowAsync(test.UserId, test.Organisation.Id, true);

        Assert.Empty(first.Failures);
        Assert.True(first.FullReconciliation);
        Assert.Equal(3, await test.Db.XeroMirrorRecords.CountAsync());
        var account = await test.Db.LedgerAccounts.SingleAsync(x => x.Code == "6200");
        var contact = await test.Db.BusinessParties.SingleAsync(x => x.Name == "Xero Customer");
        var item = await test.Db.ProductItems.SingleAsync(x => x.Code == "CONSULT");
        Assert.Equal("Office expenses", account.Name);
        Assert.Equal("first@example.test", contact.Email);
        Assert.Equal(42m, item.SalePrice);
        Assert.Equal(3, await test.Db.XeroMirrorEntityLinks.CountAsync());
        Assert.DoesNotContain(handler.Requests, request => request.Method is not ("GET" or "POST_TOKEN"));

        await sync.SetEnabledAsync(test.UserId, test.Organisation.Id, false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ChartOfAccountsService(test.Db, test.Access)
            .UpdateAsync(test.UserId, new UpdateLedgerAccountRequest(
                test.Organisation.Id, account.Id, "Local edit", account.Type,
                account.IsBankAccount, account.BankAccountKind, account.BankAccountNumber)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new BusinessPartyService(test.Db, test.Access)
            .UpdateEmailsAsync(test.UserId, new UpdateBusinessPartyEmailsRequest(
                test.Organisation.Id, contact.Id, "local@example.test", "local@example.test")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ProductCatalogService(test.Db, test.Access)
            .SetActiveAsync(test.UserId, test.Organisation.Id, item.Id, false));
        await sync.SetEnabledAsync(test.UserId, test.Organisation.Id, true);

        handler.Revision = 2;
        var second = await sync.RunNowAsync(test.UserId, test.Organisation.Id);

        Assert.Empty(second.Failures);
        Assert.False(second.FullReconciliation);
        Assert.Equal(3, await test.Db.XeroMirrorRecords.CountAsync());
        Assert.Equal("second@example.test", (await test.Db.BusinessParties.SingleAsync(x => x.Name == "Xero Customer")).Email);
        Assert.Equal(3, await test.Db.XeroMirrorEntityLinks.CountAsync());
        var connection = await test.Db.XeroConnections.SingleAsync();
        Assert.True(connection.ContinuousSyncEnabled);
        Assert.NotNull(connection.LastSyncedAt);
        Assert.NotNull(connection.NextSyncAt);
        Assert.Null(connection.LastSyncError);
        var status = await sync.GetStatusAsync(test.UserId, test.Organisation.Id);
        Assert.Equal(2, status.RecentRuns.Count);
        Assert.True(status.RecentRuns[0].StartedAt >= status.RecentRuns[1].StartedAt);
        Assert.Equal(3, status.Datasets.Sum(x => x.ActiveRecords));
    }

    [Fact]
    public async Task Full_reconciliation_archives_records_removed_from_Xero()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var tenantId = Guid.NewGuid();
        var handler = new ReadOnlyXeroHandler(tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var options = Options.Create(new XeroApiOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://account.example/api/integrations/xero/callback"
        });
        var api = new XeroApiClient(new HttpClient(handler), options);
        var connectionService = new XeroConnectionService(
            test.Db, test.Access, api, new DatabaseImmutableDocumentStore(test.Db),
            new EphemeralDataProtectionProvider(), options);
        var sync = new XeroReadOnlySyncService(
            test.Db, test.Access, api, connectionService, options,
            NullLogger<XeroReadOnlySyncService>.Instance);
        var authorize = new Uri(await connectionService.CreateAuthorizationUriAsync(test.UserId, test.Organisation.Id));
        var state = Assert.Single(QueryHelpers.ParseQuery(authorize.Query)["state"]);
        await connectionService.CompleteAuthorizationAsync(test.UserId, "code", state!);
        await connectionService.SelectTenantAsync(test.UserId, test.Organisation.Id, tenantId);

        await sync.RunNowAsync(test.UserId, test.Organisation.Id, true);
        handler.ReturnNoRecords = true;
        var result = await sync.RunNowAsync(test.UserId, test.Organisation.Id, true);

        Assert.Equal(3, result.ArchivedCount);
        Assert.Equal(3, await test.Db.XeroMirrorRecords.CountAsync(x => x.IsArchived));
    }

    [Fact]
    public async Task Daily_rate_limit_stops_the_run_and_schedules_retry_after_Xero_wait_period()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var tenantId = Guid.NewGuid();
        var handler = new RateLimitedXeroHandler(tenantId);
        var options = Options.Create(new XeroApiOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://account.example/api/integrations/xero/callback"
        });
        var api = new XeroApiClient(new HttpClient(handler), options);
        var connectionService = new XeroConnectionService(
            test.Db, test.Access, api, new DatabaseImmutableDocumentStore(test.Db),
            new EphemeralDataProtectionProvider(), options);
        var sync = new XeroReadOnlySyncService(
            test.Db, test.Access, api, connectionService, options,
            NullLogger<XeroReadOnlySyncService>.Instance);
        var authorize = new Uri(await connectionService.CreateAuthorizationUriAsync(test.UserId, test.Organisation.Id));
        var state = Assert.Single(QueryHelpers.ParseQuery(authorize.Query)["state"]);
        await connectionService.CompleteAuthorizationAsync(test.UserId, "code", state!);

        var startedAt = DateTimeOffset.UtcNow;
        var result = await sync.RunNowAsync(test.UserId, test.Organisation.Id, true);

        var failure = Assert.Single(result.Failures);
        Assert.Equal("Chart of accounts", failure.Dataset);
        Assert.Contains("paused for about 2 minute", failure.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, handler.AccountingRequests);
        var connection = await test.Db.XeroConnections.SingleAsync();
        Assert.NotNull(connection.NextSyncAt);
        Assert.True(connection.NextSyncAt >= startedAt.AddSeconds(110));
        Assert.Contains("daily limit", connection.LastSyncError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Short_rate_limit_waits_and_continues_the_same_run()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var tenantId = Guid.NewGuid();
        var handler = new TransientRateLimitedXeroHandler(tenantId);
        var options = Options.Create(new XeroApiOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://account.example/api/integrations/xero/callback"
        });
        var api = new XeroApiClient(new HttpClient(handler), options);
        var connectionService = new XeroConnectionService(
            test.Db, test.Access, api, new DatabaseImmutableDocumentStore(test.Db),
            new EphemeralDataProtectionProvider(), options);
        var delays = new List<TimeSpan>();
        var sync = new XeroReadOnlySyncService(
            test.Db, test.Access, api, connectionService, options,
            NullLogger<XeroReadOnlySyncService>.Instance)
        {
            DelayAsync = (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            }
        };
        var authorize = new Uri(await connectionService.CreateAuthorizationUriAsync(test.UserId, test.Organisation.Id));
        var state = Assert.Single(QueryHelpers.ParseQuery(authorize.Query)["state"]);
        await connectionService.CompleteAuthorizationAsync(test.UserId, "code", state!);

        var result = await sync.RunNowAsync(test.UserId, test.Organisation.Id, true);

        Assert.Empty(result.Failures);
        Assert.Equal(16, result.DatasetCount);
        Assert.Equal(2, handler.AccountRequests);
        Assert.Equal([TimeSpan.FromMinutes(2)], delays);
        var connection = await test.Db.XeroConnections.SingleAsync();
        Assert.Null(connection.LastSyncError);
        Assert.NotNull(connection.LastFullReconciliationAt);
        Assert.Equal(XeroReadOnlySyncRunStatus.Completed,
            (await test.Db.XeroReadOnlySyncRuns.SingleAsync()).Status);
    }

    [Fact]
    public async Task Repeated_short_rate_limit_stops_after_one_inline_retry()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var tenantId = Guid.NewGuid();
        var handler = new PersistentRateLimitedXeroHandler(tenantId);
        var options = Options.Create(new XeroApiOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://account.example/api/integrations/xero/callback"
        });
        var api = new XeroApiClient(new HttpClient(handler), options);
        var connectionService = new XeroConnectionService(
            test.Db, test.Access, api, new DatabaseImmutableDocumentStore(test.Db),
            new EphemeralDataProtectionProvider(), options);
        var delays = new List<TimeSpan>();
        var sync = new XeroReadOnlySyncService(
            test.Db, test.Access, api, connectionService, options,
            NullLogger<XeroReadOnlySyncService>.Instance)
        {
            DelayAsync = (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            }
        };
        var authorize = new Uri(await connectionService.CreateAuthorizationUriAsync(test.UserId, test.Organisation.Id));
        var state = Assert.Single(QueryHelpers.ParseQuery(authorize.Query)["state"]);
        await connectionService.CompleteAuthorizationAsync(test.UserId, "code", state!);

        var startedAt = DateTimeOffset.UtcNow;
        var result = await sync.RunNowAsync(test.UserId, test.Organisation.Id, true);

        var failure = Assert.Single(result.Failures);
        Assert.Equal("Chart of accounts", failure.Dataset);
        Assert.Equal(2, handler.AccountRequests);
        Assert.Equal([TimeSpan.FromMinutes(2)], delays);
        var connection = await test.Db.XeroConnections.SingleAsync();
        Assert.NotNull(connection.NextSyncAt);
        Assert.True(connection.NextSyncAt >= startedAt.AddSeconds(110));
        Assert.Equal(XeroReadOnlySyncRunStatus.Partial,
            (await test.Db.XeroReadOnlySyncRuns.SingleAsync()).Status);
    }

    [Fact]
    public async Task Inline_rate_limit_retry_budget_is_shared_across_the_whole_run()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var tenantId = Guid.NewGuid();
        var handler = new CrossDatasetRateLimitedXeroHandler(tenantId);
        var options = Options.Create(new XeroApiOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://account.example/api/integrations/xero/callback"
        });
        var api = new XeroApiClient(new HttpClient(handler), options);
        var connectionService = new XeroConnectionService(
            test.Db, test.Access, api, new DatabaseImmutableDocumentStore(test.Db),
            new EphemeralDataProtectionProvider(), options);
        var delays = new List<TimeSpan>();
        var sync = new XeroReadOnlySyncService(
            test.Db, test.Access, api, connectionService, options,
            NullLogger<XeroReadOnlySyncService>.Instance)
        {
            DelayAsync = (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            }
        };
        var authorize = new Uri(await connectionService.CreateAuthorizationUriAsync(test.UserId, test.Organisation.Id));
        var state = Assert.Single(QueryHelpers.ParseQuery(authorize.Query)["state"]);
        await connectionService.CompleteAuthorizationAsync(test.UserId, "code", state!);

        var result = await sync.RunNowAsync(test.UserId, test.Organisation.Id, true);

        var failure = Assert.Single(result.Failures);
        Assert.Equal("Tax rates", failure.Dataset);
        Assert.Equal(2, handler.AccountRequests);
        Assert.Equal(1, handler.TaxRateRequests);
        Assert.Equal([TimeSpan.FromMinutes(2)], delays);
        Assert.Equal(XeroReadOnlySyncRunStatus.Partial,
            (await test.Db.XeroReadOnlySyncRuns.SingleAsync()).Status);
    }

    [Fact]
    public async Task Provider_timeout_stops_the_run_and_schedules_one_retry()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var tenantId = Guid.NewGuid();
        var handler = new TimeoutReadOnlyXeroHandler(tenantId);
        var options = Options.Create(new XeroApiOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://account.example/api/integrations/xero/callback"
        });
        var api = new XeroApiClient(new HttpClient(handler), options);
        var connectionService = new XeroConnectionService(
            test.Db, test.Access, api, new DatabaseImmutableDocumentStore(test.Db),
            new EphemeralDataProtectionProvider(), options);
        var sync = new XeroReadOnlySyncService(
            test.Db, test.Access, api, connectionService, options,
            NullLogger<XeroReadOnlySyncService>.Instance);
        var authorize = new Uri(await connectionService.CreateAuthorizationUriAsync(test.UserId, test.Organisation.Id));
        var state = Assert.Single(QueryHelpers.ParseQuery(authorize.Query)["state"]);
        await connectionService.CompleteAuthorizationAsync(test.UserId, "code", state!);
        var startedAt = DateTimeOffset.UtcNow;

        var result = await sync.RunNowAsync(test.UserId, test.Organisation.Id, true);

        var failure = Assert.Single(result.Failures);
        Assert.Equal("Chart of accounts", failure.Dataset);
        Assert.Contains("timed out", failure.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, handler.AccountingRequests);
        var connection = await test.Db.XeroConnections.SingleAsync();
        Assert.True(connection.NextSyncAt >= startedAt.AddMinutes(5));
        Assert.Equal(XeroReadOnlySyncRunStatus.Partial,
            (await test.Db.XeroReadOnlySyncRuns.SingleAsync()).Status);
    }

    [Fact]
    public async Task Dataset_deadline_stops_an_inline_provider_wait_and_schedules_retry()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var tenantId = Guid.NewGuid();
        var handler = new PersistentRateLimitedXeroHandler(tenantId);
        var options = Options.Create(new XeroApiOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://account.example/api/integrations/xero/callback",
            DatasetTimeoutSeconds = 1
        });
        var api = new XeroApiClient(new HttpClient(handler), options);
        var connectionService = new XeroConnectionService(
            test.Db, test.Access, api, new DatabaseImmutableDocumentStore(test.Db),
            new EphemeralDataProtectionProvider(), options);
        var sync = new XeroReadOnlySyncService(
            test.Db, test.Access, api, connectionService, options,
            NullLogger<XeroReadOnlySyncService>.Instance);
        var authorize = new Uri(await connectionService.CreateAuthorizationUriAsync(test.UserId, test.Organisation.Id));
        var state = Assert.Single(QueryHelpers.ParseQuery(authorize.Query)["state"]);
        await connectionService.CompleteAuthorizationAsync(test.UserId, "code", state!);
        var startedAt = DateTimeOffset.UtcNow;

        var result = await sync.RunNowAsync(test.UserId, test.Organisation.Id, true);

        var failure = Assert.Single(result.Failures);
        Assert.Equal("Chart of accounts", failure.Dataset);
        Assert.Contains("safe import window", failure.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, handler.AccountRequests);
        var connection = await test.Db.XeroConnections.SingleAsync();
        Assert.True(connection.NextSyncAt >= startedAt.AddMinutes(5));
        Assert.Equal(XeroReadOnlySyncRunStatus.Partial,
            (await test.Db.XeroReadOnlySyncRuns.SingleAsync()).Status);
    }

    [Fact]
    public async Task New_run_closes_interrupted_running_history_before_resuming()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var tenantId = Guid.NewGuid();
        var handler = new ReadOnlyXeroHandler(tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var options = Options.Create(new XeroApiOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://account.example/api/integrations/xero/callback"
        });
        var api = new XeroApiClient(new HttpClient(handler), options);
        var connectionService = new XeroConnectionService(
            test.Db, test.Access, api, new DatabaseImmutableDocumentStore(test.Db),
            new EphemeralDataProtectionProvider(), options);
        var sync = new XeroReadOnlySyncService(
            test.Db, test.Access, api, connectionService, options,
            NullLogger<XeroReadOnlySyncService>.Instance);
        var authorize = new Uri(await connectionService.CreateAuthorizationUriAsync(test.UserId, test.Organisation.Id));
        var state = Assert.Single(QueryHelpers.ParseQuery(authorize.Query)["state"]);
        await connectionService.CompleteAuthorizationAsync(test.UserId, "code", state!);
        var connection = await test.Db.XeroConnections.SingleAsync();
        connection.SyncLeaseUntil = DateTimeOffset.UtcNow.AddMinutes(-1);
        var interrupted = new XeroReadOnlySyncRun
        {
            OrganisationId = test.Organisation.Id,
            XeroConnectionId = connection.Id,
            Trigger = "scheduled",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-45)
        };
        test.Db.XeroReadOnlySyncRuns.Add(interrupted);
        await test.Db.SaveChangesAsync();

        var result = await sync.RunNowAsync(test.UserId, test.Organisation.Id, true);

        Assert.Empty(result.Failures);
        Assert.Equal(XeroReadOnlySyncRunStatus.Failed, interrupted.Status);
        Assert.NotNull(interrupted.CompletedAt);
        Assert.Contains("interrupted", interrupted.ErrorSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(XeroReadOnlySyncRunStatus.Completed,
            (await test.Db.XeroReadOnlySyncRuns.SingleAsync(x => x.Id != interrupted.Id)).Status);
    }

    [Fact]
    public async Task Active_sync_is_reported_without_turning_a_parallel_refresh_into_an_error()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var tenantId = Guid.NewGuid();
        var handler = new ReadOnlyXeroHandler(tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var options = Options.Create(new XeroApiOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://account.example/api/integrations/xero/callback"
        });
        var api = new XeroApiClient(new HttpClient(handler), options);
        var connectionService = new XeroConnectionService(
            test.Db, test.Access, api, new DatabaseImmutableDocumentStore(test.Db),
            new EphemeralDataProtectionProvider(), options);
        var sync = new XeroReadOnlySyncService(
            test.Db, test.Access, api, connectionService, options,
            NullLogger<XeroReadOnlySyncService>.Instance);
        var authorize = new Uri(await connectionService.CreateAuthorizationUriAsync(test.UserId, test.Organisation.Id));
        var state = Assert.Single(QueryHelpers.ParseQuery(authorize.Query)["state"]);
        await connectionService.CompleteAuthorizationAsync(test.UserId, "code", state!);
        var connection = await test.Db.XeroConnections.SingleAsync();
        connection.SyncLeaseUntil = DateTimeOffset.UtcNow.AddMinutes(5);
        connection.UpdatedByUserId = "system:xero-read-only-sync:active";
        await test.Db.SaveChangesAsync();

        var status = await sync.GetStatusAsync(test.UserId, test.Organisation.Id);
        var result = await sync.RunNowAsync(test.UserId, test.Organisation.Id, true);

        Assert.True(status.IsRunning);
        Assert.True(result.AlreadyRunning);
        Assert.True(result.FullReconciliation);
        Assert.Empty(result.Failures);
        Assert.Empty(await test.Db.XeroReadOnlySyncRuns.ToListAsync());
    }

    [Fact]
    public async Task Rejected_refresh_token_pauses_automatic_sync_until_reconnected()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var tenantId = Guid.NewGuid();
        var handler = new RejectedRefreshTokenXeroHandler(tenantId);
        var options = Options.Create(new XeroApiOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://account.example/api/integrations/xero/callback"
        });
        var api = new XeroApiClient(new HttpClient(handler), options);
        var connectionService = new XeroConnectionService(
            test.Db, test.Access, api, new DatabaseImmutableDocumentStore(test.Db),
            new EphemeralDataProtectionProvider(), options);
        var sync = new XeroReadOnlySyncService(
            test.Db, test.Access, api, connectionService, options,
            NullLogger<XeroReadOnlySyncService>.Instance);
        var authorize = new Uri(await connectionService.CreateAuthorizationUriAsync(test.UserId, test.Organisation.Id));
        var state = Assert.Single(QueryHelpers.ParseQuery(authorize.Query)["state"]);
        await connectionService.CompleteAuthorizationAsync(test.UserId, "code", state!);
        await sync.SetEnabledAsync(test.UserId, test.Organisation.Id, true);
        var connection = await test.Db.XeroConnections.SingleAsync();
        connection.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await test.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<XeroReconnectRequiredException>(() =>
            sync.RunNowAsync(test.UserId, test.Organisation.Id, true));

        Assert.Contains("Reconnect Xero", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(connection.ContinuousSyncEnabled);
        Assert.Null(connection.NextSyncAt);
        Assert.Null(connection.SyncLeaseUntil);
        Assert.Contains("Reconnect Xero", connection.LastSyncError, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(XeroReadOnlySyncRunStatus.Failed,
            (await test.Db.XeroReadOnlySyncRuns.SingleAsync()).Status);

        var reconnect = new Uri(await connectionService.CreateAuthorizationUriAsync(test.UserId, test.Organisation.Id));
        var reconnectState = Assert.Single(QueryHelpers.ParseQuery(reconnect.Query)["state"]);
        await connectionService.CompleteAuthorizationAsync(test.UserId, "new-code", reconnectState!);

        Assert.True(connection.ContinuousSyncEnabled);
        Assert.NotNull(connection.NextSyncAt);
        Assert.Null(connection.LastSyncError);
    }

    [Fact]
    public async Task Cancelled_run_releases_lease_and_records_failure()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        var tenantId = Guid.NewGuid();
        var handler = new CancellingXeroHandler(tenantId, cancellation);
        var options = Options.Create(new XeroApiOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://account.example/api/integrations/xero/callback"
        });
        var api = new XeroApiClient(new HttpClient(handler), options);
        var connectionService = new XeroConnectionService(
            test.Db, test.Access, api, new DatabaseImmutableDocumentStore(test.Db),
            new EphemeralDataProtectionProvider(), options);
        var sync = new XeroReadOnlySyncService(
            test.Db, test.Access, api, connectionService, options,
            NullLogger<XeroReadOnlySyncService>.Instance);
        var authorize = new Uri(await connectionService.CreateAuthorizationUriAsync(test.UserId, test.Organisation.Id));
        var state = Assert.Single(QueryHelpers.ParseQuery(authorize.Query)["state"]);
        await connectionService.CompleteAuthorizationAsync(test.UserId, "code", state!);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sync.RunNowAsync(test.UserId, test.Organisation.Id, true, cancellation.Token));

        test.Db.ChangeTracker.Clear();
        var connection = await test.Db.XeroConnections.SingleAsync();
        var run = await test.Db.XeroReadOnlySyncRuns.SingleAsync();
        Assert.Null(connection.SyncLeaseUntil);
        Assert.Equal(XeroReadOnlySyncRunStatus.Failed, run.Status);
        Assert.NotNull(run.CompletedAt);
    }

    [Fact]
    public async Task Worker_startup_recovers_future_lease_from_previous_process()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var tenantId = Guid.NewGuid();
        var handler = new ReadOnlyXeroHandler(tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var options = Options.Create(new XeroApiOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://account.example/api/integrations/xero/callback"
        });
        var api = new XeroApiClient(new HttpClient(handler), options);
        var connectionService = new XeroConnectionService(
            test.Db, test.Access, api, new DatabaseImmutableDocumentStore(test.Db),
            new EphemeralDataProtectionProvider(), options);
        var sync = new XeroReadOnlySyncService(
            test.Db, test.Access, api, connectionService, options,
            NullLogger<XeroReadOnlySyncService>.Instance);
        var authorize = new Uri(await connectionService.CreateAuthorizationUriAsync(test.UserId, test.Organisation.Id));
        var state = Assert.Single(QueryHelpers.ParseQuery(authorize.Query)["state"]);
        await connectionService.CompleteAuthorizationAsync(test.UserId, "code", state!);
        var connection = await test.Db.XeroConnections.SingleAsync();
        connection.SyncLeaseUntil = DateTimeOffset.UtcNow.AddMinutes(25);
        connection.SyncLeaseOwner = "system:xero-read-only-sync:previous-process";
        var interrupted = new XeroReadOnlySyncRun
        {
            OrganisationId = test.Organisation.Id,
            XeroConnectionId = connection.Id,
            Trigger = "scheduled",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-20)
        };
        test.Db.XeroReadOnlySyncRuns.Add(interrupted);
        await test.Db.SaveChangesAsync();

        await sync.RecoverLegacyLeasesAsync();

        Assert.Null(connection.SyncLeaseUntil);
        Assert.Null(connection.SyncLeaseOwner);
        Assert.NotNull(connection.NextSyncAt);
        Assert.Equal(XeroReadOnlySyncRunStatus.Failed, interrupted.Status);
        Assert.NotNull(interrupted.CompletedAt);
        Assert.Contains("restart", interrupted.ErrorSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refreshing_an_expired_access_token_does_not_break_the_sync_lease()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var tenantId = Guid.NewGuid();
        var handler = new ReadOnlyXeroHandler(tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var options = Options.Create(new XeroApiOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://account.example/api/integrations/xero/callback"
        });
        var api = new XeroApiClient(new HttpClient(handler), options);
        var connectionService = new XeroConnectionService(
            test.Db, test.Access, api, new DatabaseImmutableDocumentStore(test.Db),
            new EphemeralDataProtectionProvider(), options);
        var sync = new XeroReadOnlySyncService(
            test.Db, test.Access, api, connectionService, options,
            NullLogger<XeroReadOnlySyncService>.Instance);
        var authorize = new Uri(await connectionService.CreateAuthorizationUriAsync(test.UserId, test.Organisation.Id));
        var state = Assert.Single(QueryHelpers.ParseQuery(authorize.Query)["state"]);
        await connectionService.CompleteAuthorizationAsync(test.UserId, "code", state!);
        var connection = await test.Db.XeroConnections.SingleAsync();
        connection.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await test.Db.SaveChangesAsync();

        var result = await sync.RunNowAsync(test.UserId, test.Organisation.Id, true);

        Assert.Empty(result.Failures);
        Assert.Null(connection.SyncLeaseUntil);
        Assert.Null(connection.SyncLeaseOwner);
        Assert.Equal(XeroReadOnlySyncRunStatus.Completed,
            (await test.Db.XeroReadOnlySyncRuns.SingleAsync()).Status);
    }

    private sealed class ReadOnlyXeroHandler(Guid tenantId, Guid accountId, Guid contactId, Guid itemId) : HttpMessageHandler
    {
        public int Revision { get; set; } = 1;
        public bool ReturnNoRecords { get; set; }
        public List<(string Method, string Url)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri == "https://identity.xero.com/connect/token")
            {
                Requests.Add(("POST_TOKEN", request.RequestUri.AbsoluteUri));
                return Json("""
                    {"access_token":"access-token","refresh_token":"refresh-token","expires_in":1800,"scope":"offline_access accounting.settings.read accounting.contacts.read accounting.invoices.read"}
                    """);
            }
            Requests.Add((request.Method.Method, request.RequestUri.AbsoluteUri));
            if (request.RequestUri.AbsoluteUri == "https://api.xero.com/connections")
                return Json($$"""[{"id":"connection-one","tenantId":"{{tenantId}}","tenantName":"Source Books Ltd","tenantType":"ORGANISATION"}]""");
            if (ReturnNoRecords) return EmptyFor(request.RequestUri.AbsolutePath);
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("/Accounts", StringComparison.Ordinal))
                return Json($$"""{"Accounts":[{"AccountID":"{{accountId}}","Code":"6200","Name":"Office expenses","Type":"EXPENSE","Status":"ACTIVE"}]}""");
            if (path.EndsWith("/Contacts", StringComparison.Ordinal))
                return Json($$"""{"Contacts":[{"ContactID":"{{contactId}}","Name":"Xero Customer","EmailAddress":"{{(Revision == 1 ? "first" : "second")}}@example.test","IsCustomer":true,"ContactStatus":"ACTIVE","UpdatedDateUTC":"2026-10-19T00:0{{Revision}}:00Z"}]}""");
            if (path.EndsWith("/Items", StringComparison.Ordinal))
                return Json($$$"""{"Items":[{"ItemID":"{{{itemId}}}","Code":"CONSULT","Name":"Consulting","Status":"ACTIVE","SalesDetails":{"UnitPrice":42}}]}""");
            return EmptyFor(path);
        }

        private static Task<HttpResponseMessage> EmptyFor(string path)
        {
            var root = path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() switch
            {
                "TaxRates" => "TaxRates",
                "Currencies" => "Currencies",
                "TrackingCategories" => "TrackingCategories",
                "Invoices" => "Invoices",
                "CreditNotes" => "CreditNotes",
                "Payments" => "Payments",
                "Prepayments" => "Prepayments",
                "Overpayments" => "Overpayments",
                "BankTransactions" => "BankTransactions",
                "BankTransfers" => "BankTransfers",
                "ManualJournals" => "ManualJournals",
                "Accounts" => "Accounts",
                "Contacts" => "Contacts",
                "Items" => "Items",
                _ => "Records"
            };
            return Json($$"""{"{{root}}":[]}""");
        }

        private static Task<HttpResponseMessage> Json(string body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
    }

    private sealed class TimeoutReadOnlyXeroHandler(Guid tenantId) : HttpMessageHandler
    {
        public int AccountingRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri == "https://identity.xero.com/connect/token")
                return Json("""
                    {"access_token":"access-token","refresh_token":"refresh-token","expires_in":1800,"scope":"offline_access accounting.settings.read"}
                    """);
            if (request.RequestUri.AbsoluteUri == "https://api.xero.com/connections")
                return Json($$"""
                    [{"id":"connection-one","tenantId":"{{tenantId}}","tenantName":"Source Books Ltd","tenantType":"ORGANISATION"}]
                    """);
            AccountingRequests++;
            return Task.FromException<HttpResponseMessage>(new OperationCanceledException());
        }

        private static Task<HttpResponseMessage> Json(string body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
    }

    private sealed class RejectedRefreshTokenXeroHandler(Guid tenantId) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri == "https://identity.xero.com/connect/token")
            {
                var form = request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
                if (form.Contains("grant_type=refresh_token", StringComparison.Ordinal))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        ReasonPhrase = "Bad Request",
                        Content = new StringContent("{\"error\":\"invalid_grant\"}", Encoding.UTF8, "application/json")
                    });
                }
                return Json("""{"access_token":"access-token","refresh_token":"refresh-token","expires_in":1800,"scope":"offline_access accounting.settings.read"}""");
            }
            if (request.RequestUri.AbsoluteUri == "https://api.xero.com/connections")
                return Json($$"""[{"id":"connection-one","tenantId":"{{tenantId}}","tenantName":"Source Books Ltd","tenantType":"ORGANISATION"}]""");
            return Json("{}");
        }

        private static Task<HttpResponseMessage> Json(string body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
    }

    private sealed class RateLimitedXeroHandler(Guid tenantId) : HttpMessageHandler
    {
        public int AccountingRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri == "https://identity.xero.com/connect/token")
                return Json("""{"access_token":"access-token","refresh_token":"refresh-token","expires_in":1800,"scope":"offline_access accounting.settings.read"}""");
            if (request.RequestUri.AbsoluteUri == "https://api.xero.com/connections")
                return Json($$"""[{"id":"connection-one","tenantId":"{{tenantId}}","tenantName":"Source Books Ltd","tenantType":"ORGANISATION"}]""");

            AccountingRequests++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
            response.Headers.TryAddWithoutValidation("X-Rate-Limit-Problem", "day");
            return Task.FromResult(response);
        }

        private static Task<HttpResponseMessage> Json(string body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
    }

    private sealed class TransientRateLimitedXeroHandler(Guid tenantId) : HttpMessageHandler
    {
        public int AccountRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri == "https://identity.xero.com/connect/token")
                return Json("""{"access_token":"access-token","refresh_token":"refresh-token","expires_in":1800,"scope":"offline_access accounting.settings.read"}""");
            if (request.RequestUri.AbsoluteUri == "https://api.xero.com/connections")
                return Json($$"""[{"id":"connection-one","tenantId":"{{tenantId}}","tenantName":"Source Books Ltd","tenantType":"ORGANISATION"}]""");

            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("/Accounts", StringComparison.Ordinal) && ++AccountRequests == 1)
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
                response.Headers.TryAddWithoutValidation("X-Rate-Limit-Problem", "minute");
                return Task.FromResult(response);
            }

            var root = path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() switch
            {
                "TaxRates" => "TaxRates",
                "Currencies" => "Currencies",
                "TrackingCategories" => "TrackingCategories",
                "Invoices" => "Invoices",
                "CreditNotes" => "CreditNotes",
                "Payments" => "Payments",
                "Prepayments" => "Prepayments",
                "Overpayments" => "Overpayments",
                "BankTransactions" => "BankTransactions",
                "BankTransfers" => "BankTransfers",
                "ManualJournals" => "ManualJournals",
                "Accounts" => "Accounts",
                "Contacts" => "Contacts",
                "Items" => "Items",
                _ => "Records"
            };
            return Json($$"""{"{{root}}":[]}""");
        }

        private static Task<HttpResponseMessage> Json(string body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
    }

    private sealed class PersistentRateLimitedXeroHandler(Guid tenantId) : HttpMessageHandler
    {
        public int AccountRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri == "https://identity.xero.com/connect/token")
                return Json("""{"access_token":"access-token","refresh_token":"refresh-token","expires_in":1800,"scope":"offline_access accounting.settings.read"}""");
            if (request.RequestUri.AbsoluteUri == "https://api.xero.com/connections")
                return Json($$"""[{"id":"connection-one","tenantId":"{{tenantId}}","tenantName":"Source Books Ltd","tenantType":"ORGANISATION"}]""");

            AccountRequests++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
            response.Headers.TryAddWithoutValidation("X-Rate-Limit-Problem", "minute");
            return Task.FromResult(response);
        }

        private static Task<HttpResponseMessage> Json(string body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
    }

    private sealed class CrossDatasetRateLimitedXeroHandler(Guid tenantId) : HttpMessageHandler
    {
        public int AccountRequests { get; private set; }
        public int TaxRateRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri == "https://identity.xero.com/connect/token")
                return Json("""{"access_token":"access-token","refresh_token":"refresh-token","expires_in":1800,"scope":"offline_access accounting.settings.read"}""");
            if (request.RequestUri.AbsoluteUri == "https://api.xero.com/connections")
                return Json($$"""[{"id":"connection-one","tenantId":"{{tenantId}}","tenantName":"Source Books Ltd","tenantType":"ORGANISATION"}]""");

            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("/Accounts", StringComparison.Ordinal))
            {
                AccountRequests++;
                if (AccountRequests == 1) return RateLimited();
                return Json("""{"Accounts":[]}""");
            }
            if (path.EndsWith("/TaxRates", StringComparison.Ordinal))
            {
                TaxRateRequests++;
                return RateLimited();
            }
            return Json("{}");
        }

        private static Task<HttpResponseMessage> RateLimited()
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
            response.Headers.TryAddWithoutValidation("X-Rate-Limit-Problem", "minute");
            return Task.FromResult(response);
        }

        private static Task<HttpResponseMessage> Json(string body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
    }

    private sealed class CancellingXeroHandler(Guid tenantId, CancellationTokenSource cancellation) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri == "https://identity.xero.com/connect/token")
                return Json("""{"access_token":"access-token","refresh_token":"refresh-token","expires_in":1800,"scope":"offline_access accounting.settings.read"}""");
            if (request.RequestUri.AbsoluteUri == "https://api.xero.com/connections")
                return Json($$"""[{"id":"connection-one","tenantId":"{{tenantId}}","tenantName":"Source Books Ltd","tenantType":"ORGANISATION"}]""");

            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
        }

        private static Task<HttpResponseMessage> Json(string body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
    }
}
