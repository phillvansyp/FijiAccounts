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
    public async Task Asset_register_uses_Xero_type_names_and_includes_prior_depreciation()
    {
        var assetId = Guid.NewGuid();
        var assetTypeId = Guid.NewGuid();
        var bytes = await XeroFixedAssetRegister.ReadSourceAsync(endpoint =>
        {
            var response = endpoint.EndsWith("/AssetTypes", StringComparison.Ordinal)
                ? $$"""[{"assetTypeId":"{{assetTypeId}}","assetTypeName":"Computer equipment"}]"""
                : endpoint.Contains("status=REGISTERED", StringComparison.Ordinal)
                    ? $$"""
                        {"pagination":{"page":1,"pageCount":1,"itemCount":1},"items":[
                          {"assetId":"{{assetId}}","assetTypeId":"{{assetTypeId}}","assetNumber":"FA-1",
                           "assetName":"Computer","assetStatus":"Registered","purchasePrice":100,
                           "accountingBookValue":70,"bookDepreciationDetail":{
                              "priorAccumDepreciationAmount":25,"currentAccumDepreciationAmount":5
                           }}
                        ]}
                        """
                    : """{"pagination":{"page":1,"pageCount":0,"itemCount":0},"items":[]}""";
            return Task.FromResult(Encoding.UTF8.GetBytes(response));
        }, CancellationToken.None);

        using var payload = System.Text.Json.JsonDocument.Parse(bytes);
        var asset = Assert.Single(XeroFixedAssetRegister.ReadSnapshot(
            payload.RootElement.GetProperty("registers")[0].GetRawText()));
        Assert.Equal("Computer equipment", asset.AssetTypeName);
        Assert.Equal(30m, asset.BookDepreciationDetail?.AccumulatedDepreciationAmount);
        Assert.Equal(asset.PurchasePrice - asset.AccountingBookValue,
            asset.BookDepreciationDetail?.AccumulatedDepreciationAmount);
    }

    [Fact]
    public void Bank_rules_preserve_source_priority_and_reject_duplicate_source_ids()
    {
        var ruleId = Guid.NewGuid();
        var rule = new XeroBankRuleSnapshot.Rule(ruleId, "Example", "Spend", 53, "All",
            [new("Payee", "equals", "Example"), new("Description", "contains", "Office")],
            [new("Percentage", "Office supplies", "6200 – Office expenses", "GST on Expenses", "Head office", "100")],
            "Payee", "Description", "All bank accounts");
        var snapshot = new XeroBankRuleSnapshot("!test", [rule]);
        var parsed = XeroBankRuleSnapshot.Parse(snapshot.Serialise());
        var copied = Assert.Single(parsed.Rules);
        Assert.Equal(53, copied.Order);
        Assert.Equal(rule.Conditions.ToArray(), copied.Conditions.ToArray());
        Assert.Equal(rule.Allocations.ToArray(), copied.Allocations.ToArray());
        Assert.Equal($"https://go.xero.com/app/!test/bank-rules/edit/{ruleId:D}", parsed.SourceLink(copied).AbsoluteUri);
        Assert.Throws<InvalidOperationException>(() => XeroBankRuleSnapshot.Parse(
            (snapshot with { Rules = [rule, rule with { Order = 54 }] }).Serialise()));
    }

    [Fact]
    public async Task Fixed_asset_register_waits_for_all_pages_and_preserves_source_amounts()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var seen = new List<string>();
        var bytes = await XeroFixedAssetRegister.ReadSourceAsync(endpoint =>
        {
            var query = QueryHelpers.ParseQuery(new Uri(endpoint).Query);
            var status = query["status"].ToString();
            var page = int.Parse(query["page"].ToString());
            seen.Add($"{status}:{page}");
            var (count, pages, asset) = status switch
            {
                "REGISTERED" => (2, 2, Asset(ids[page - 1], "Registered")),
                "DISPOSED" => (1, 1, Asset(ids[2], "Disposed")),
                _ => (0, 0, "")
            };
            return Task.FromResult(Encoding.UTF8.GetBytes(
                $$"""{"pagination":{"page":{{page}},"pageCount":{{pages}},"itemCount":{{count}}},"items":[{{asset}}]}"""));
        }, CancellationToken.None);

        Assert.Equal(["DRAFT:1", "REGISTERED:1", "REGISTERED:2", "DISPOSED:1"], seen);
        using var document = System.Text.Json.JsonDocument.Parse(bytes);
        var assets = XeroFixedAssetRegister.ReadSnapshot(document.RootElement.GetProperty("registers")[0].GetRawText());
        Assert.Equal(ids, assets.Select(x => x.AssetId));
        Assert.Equal(28.50m, assets[0].AccountingBookValue);
        Assert.Equal(71.50m, assets[0].BookDepreciationDetail?.CurrentAccumDepreciationAmount);
        Assert.Equal("Disposed", assets[2].AssetStatus);

        static string Asset(Guid id, string status) =>
            $$$"""{"assetId":"{{{id}}}","assetName":"Asset","assetStatus":"{{{status}}}","accountingBookValue":28.50,"bookDepreciationDetail":{"currentAccumDepreciationAmount":71.50}}""";
    }

    [Fact]
    public void Fixed_asset_export_keeps_the_Xero_date_convention_and_disposal_status()
    {
        const string header = "*AssetName,*AssetNumber,AssetStatus,PurchaseDate,PurchasePrice,AssetType,Book_BookValue,AccumulatedDepreciation,DisposalDate";
        const string row = "\"Bench, two seats\",FA-1,Disposed,1/02/2025,100.00,Office,18.75,81.25,20/11/2025";
        var assets = XeroFixedAssetRegister.ReadExport(header + "\n" + row);
        var asset = Assert.Single(XeroFixedAssetRegister.ReadSnapshot(XeroFixedAssetRegister.CreateExportSnapshot(assets)));
        Assert.Equal("Bench, two seats", asset.AssetName);
        Assert.Equal("2025-02-01", asset.PurchaseDate);
        Assert.Equal("2025-11-20", asset.DisposalDate);
        Assert.Equal("Disposed", asset.AssetStatus);
        Assert.Equal(18.75m, asset.AccountingBookValue);
        Assert.Equal(81.25m, asset.BookDepreciationDetail?.CurrentAccumDepreciationAmount);
        Assert.Throws<InvalidOperationException>(() => XeroFixedAssetRegister.ReadExport(header + "\n" + row + "\n" + row));
    }

    [Fact]
    public async Task Large_datasets_continue_beyond_ten_thousand_records_and_items_are_read_once()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var tenantId = Guid.NewGuid();
        var handler = new LargeReadOnlyXeroHandler(tenantId);
        var options = Options.Create(new XeroApiOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://account.example/api/integrations/xero/callback",
            DatasetTimeoutSeconds = 1_800
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

        var result = await sync.RunNowAsync(test.UserId, test.Organisation.Id, true);

        Assert.Empty(result.Failures);
        Assert.Equal(17, result.DatasetCount);
        Assert.Equal(10_250, await test.Db.XeroMirrorRecords.CountAsync(x => x.DatasetKey == "sales-invoices"));
        Assert.Equal(127, await test.Db.XeroMirrorRecords.CountAsync(x => x.DatasetKey == "items"));
        Assert.Equal(42, handler.SalesInvoiceRequests);
        Assert.Equal(1, handler.ItemRequests);
        Assert.All(handler.RequestedPageSizes, size => Assert.Equal(250, size));
        Assert.NotNull((await test.Db.XeroConnections.SingleAsync()).LastFullReconciliationAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Historical_scan_resumes_saved_pages_and_checks_missing_invoices(bool shifted)
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var tenantId = Guid.NewGuid();
        var handler = new LargeReadOnlyXeroHandler(tenantId) { InvoicePages = 3, RateLimitedPage = 3 };
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
        var sync = new XeroReadOnlySyncService(test.Db, test.Access, api, connectionService, options,
            NullLogger<XeroReadOnlySyncService>.Instance);
        var authorize = new Uri(await connectionService.CreateAuthorizationUriAsync(test.UserId, test.Organisation.Id));
        var state = Assert.Single(QueryHelpers.ParseQuery(authorize.Query)["state"]);
        await connectionService.CompleteAuthorizationAsync(test.UserId, "code", state!);

        var interrupted = await sync.RunNowAsync(test.UserId, test.Organisation.Id, true);

        Assert.Single(interrupted.Failures);
        Assert.True(interrupted.RecordCount >= 500);
        Assert.Equal(500, await test.Db.XeroMirrorRecords.CountAsync(x => x.DatasetKey == "sales-invoices"));
        Assert.False(await test.Db.XeroMirrorRecords.AnyAsync(x => x.IsArchived));
        var connection = await test.Db.XeroConnections.SingleAsync();
        Assert.NotNull(connection.SyncCheckpointJson);
        Assert.Null(connection.LastFullReconciliationAt);
        var scanStarted = (await test.Db.XeroReadOnlySyncRuns.SingleAsync()).StartedAt;
        test.Db.ChangeTracker.Clear();

        handler.RateLimitedPage = null;
        handler.ShiftInvoices = shifted;
        handler.InvoicePageRequests.Clear();
        var completed = await sync.RunNowAsync(test.UserId, test.Organisation.Id);

        Assert.Empty(completed.Failures);
        Assert.Equal(17, completed.DatasetCount);
        Assert.Equal(1, handler.ItemRequests);
        Assert.Equal(shifted ? new[] { 2, 1, 1, 2, 3 } : [2, 3, 4], handler.InvoicePageRequests);
        var invoices = test.Db.XeroMirrorRecords.Where(x => x.DatasetKey == "sales-invoices");
        Assert.Equal(750, await invoices.CountAsync());
        Assert.Equal(shifted ? 1 : 0, await invoices.CountAsync(x => x.IsArchived));
        Assert.False((await invoices.SingleAsync(x => x.ExternalId == LargeReadOnlyXeroHandler.InvoiceId(2))).IsArchived);
        var finished = await test.Db.XeroConnections.SingleAsync();
        Assert.Null(finished.SyncCheckpointJson);
        Assert.NotNull(finished.LastFullReconciliationAt);
        Assert.Equal(scanStarted, finished.LastSyncedAt);
    }

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
        var handler = new ReadOnlyXeroHandler(tenantId, accountId, contactId, itemId)
        {
            HasArchivedContact = true,
            HasContactGroup = true
        };
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
        Assert.Equal(5, await test.Db.XeroMirrorRecords.CountAsync());
        var copiedGroup = await test.Db.XeroMirrorRecords.SingleAsync(x => x.DatasetKey == XeroContactGroups.Key);
        var xeroGroup = XeroContactGroups.ReadSaved(copiedGroup.SourceJson);
        Assert.Equal("Preferred customers", xeroGroup.Name);
        Assert.Equal(new XeroContactGroups.Member(contactId, "Xero Customer"), Assert.Single(xeroGroup.Contacts));
        var account = await test.Db.LedgerAccounts.SingleAsync(x => x.Code == "6200");
        var contact = await test.Db.BusinessParties.SingleAsync(x => x.Name == "Xero Customer");
        var archivedContact = await test.Db.BusinessParties.SingleAsync(x => x.Name == "Archived Xero Customer");
        var item = await test.Db.ProductItems.SingleAsync(x => x.Code == "CONSULT");
        Assert.Equal("Office expenses", account.Name);
        Assert.Equal("first@example.test", contact.Email);
        Assert.Equal(contact.Email, archivedContact.Email);
        Assert.True(contact.IsActive);
        Assert.False(archivedContact.IsActive);
        Assert.Equal(42m, item.SalePrice);
        Assert.Equal(4, await test.Db.XeroMirrorEntityLinks.CountAsync());
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
        Assert.Equal(5, await test.Db.XeroMirrorRecords.CountAsync());
        Assert.Equal("second@example.test", (await test.Db.BusinessParties.SingleAsync(x => x.Name == "Xero Customer")).Email);
        Assert.Equal(4, await test.Db.XeroMirrorEntityLinks.CountAsync());
        var connection = await test.Db.XeroConnections.SingleAsync();
        Assert.True(connection.ContinuousSyncEnabled);
        Assert.NotNull(connection.LastSyncedAt);
        Assert.NotNull(connection.NextSyncAt);
        Assert.Null(connection.LastSyncError);
        var status = await sync.GetStatusAsync(test.UserId, test.Organisation.Id);
        Assert.Equal(2, status.RecentRuns.Count);
        Assert.True(status.RecentRuns[0].StartedAt >= status.RecentRuns[1].StartedAt);
        Assert.Equal(4, status.Datasets.Sum(x => x.ActiveRecords));
        Assert.Equal(1, status.Datasets.Single(x => x.Key == "contacts").ArchivedRecords);

        handler.HasContactGroup = false;
        var third = await sync.RunNowAsync(test.UserId, test.Organisation.Id, true);
        Assert.Empty(third.Failures);
        Assert.True((await test.Db.XeroMirrorRecords.SingleAsync(x => x.Id == copiedGroup.Id)).IsArchived);
    }

    [Fact]
    public async Task Product_statuses_from_Xero_are_saved_when_the_API_omits_status()
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
        var connections = new XeroConnectionService(test.Db, test.Access, api,
            new DatabaseImmutableDocumentStore(test.Db), new EphemeralDataProtectionProvider(), options);
        var sync = new XeroReadOnlySyncService(test.Db, test.Access, api, connections, options,
            NullLogger<XeroReadOnlySyncService>.Instance);
        var authorize = new Uri(await connections.CreateAuthorizationUriAsync(test.UserId, test.Organisation.Id));
        var state = Assert.Single(QueryHelpers.ParseQuery(authorize.Query)["state"]);
        await connections.CompleteAuthorizationAsync(test.UserId, "code", state!);
        Assert.Empty((await sync.RunNowAsync(test.UserId, test.Organisation.Id)).Failures);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sync.ApplyXeroItemStatusesAsync(test.UserId, test.Organisation.Id, tenantId, "", "OTHER"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sync.ApplyXeroItemStatusesAsync(test.UserId, test.Organisation.Id, tenantId, "CONSULT", "CONSULT"));
        Assert.True((await test.Db.ProductItems.SingleAsync()).IsActive);

        var updated = await sync.ApplyXeroItemStatusesAsync(
            test.UserId, test.Organisation.Id, tenantId, "", "CONSULT");
        Assert.Equal(1, updated);

        handler.OmitItemStatus = true;
        Assert.Empty((await sync.RunNowAsync(test.UserId, test.Organisation.Id)).Failures);
        Assert.False((await test.Db.ProductItems.SingleAsync()).IsActive);
        Assert.True((await test.Db.XeroMirrorRecords.SingleAsync(x => x.DatasetKey == "items")).IsArchived);

        handler.OmitItemStatus = false;
        Assert.Empty((await sync.RunNowAsync(test.UserId, test.Organisation.Id)).Failures);
        Assert.True((await test.Db.ProductItems.SingleAsync()).IsActive);
        Assert.False((await test.Db.XeroMirrorRecords.SingleAsync(x => x.DatasetKey == "items")).IsArchived);
    }

    [Fact]
    public async Task Xero_account_codes_that_collide_after_local_limit_share_one_local_account()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var tenantId = Guid.NewGuid();
        var handler = new ReadOnlyXeroHandler(tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())
        {
            ReturnDuplicateAccountCode = true
        };
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

        var result = await sync.RunNowAsync(test.UserId, test.Organisation.Id, true);

        Assert.Empty(result.Failures);
        var account = await test.Db.LedgerAccounts.SingleAsync(x => x.Code == "12345678901234567890");
        Assert.True(account.IsActive);
        Assert.Equal("Office expenses", account.Name);
        Assert.Equal(2, await test.Db.XeroMirrorEntityLinks.CountAsync(x => x.DatasetKey == "accounts"));
        Assert.All(
            await test.Db.XeroMirrorEntityLinks.Where(x => x.DatasetKey == "accounts").ToListAsync(),
            link => Assert.Equal(account.Id, link.TargetEntityId));
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
    public async Task A_later_request_can_retry_after_another_short_rate_limit()
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

        Assert.Empty(result.Failures);
        Assert.Equal(2, handler.AccountRequests);
        Assert.Equal(2, handler.TaxRateRequests);
        Assert.Equal([TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2)], delays);
        Assert.Equal(XeroReadOnlySyncRunStatus.Completed,
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

    private sealed class LargeReadOnlyXeroHandler(Guid tenantId) : HttpMessageHandler
    {
        public int SalesInvoiceRequests { get; private set; }
        public int ItemRequests { get; private set; }
        public List<int> RequestedPageSizes { get; } = [];
        public List<int> InvoicePageRequests { get; } = [];
        public int InvoicePages { get; init; } = 41;
        public int? RateLimitedPage { get; set; }
        public bool ShiftInvoices { get; set; }

        public static string InvoiceId(int index) => new Guid(index, 0, 0, new byte[8]).ToString("D");

        private static string Invoice(int index) =>
            $$"""{"InvoiceID":"{{InvoiceId(index)}}","InvoiceNumber":"INV-{{index:000000}}","Status":"AUTHORISED"}""";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri == "https://identity.xero.com/connect/token")
                return Json("""{"access_token":"access-token","refresh_token":"refresh-token","expires_in":1800,"scope":"offline_access accounting.settings.read accounting.contacts.read accounting.invoices.read"}""");
            if (request.RequestUri.AbsoluteUri == "https://api.xero.com/connections")
                return Json($$"""[{"id":"connection-one","tenantId":"{{tenantId}}","tenantName":"Source Books Ltd","tenantType":"ORGANISATION"}]""");

            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("/Items", StringComparison.Ordinal))
            {
                ItemRequests++;
                var items = string.Join(',', Enumerable.Range(1, 127)
                    .Select(index => $$"""{"ItemID":"item-{{index}}","Code":"ITEM{{index:000}}","Name":"Item {{index}}","Status":"ACTIVE"}"""));
                return Json($$"""{"Items":[{{items}}]}""");
            }

            if (path.EndsWith("/Invoices", StringComparison.Ordinal) &&
                request.RequestUri.Query.Contains("ACCREC", StringComparison.OrdinalIgnoreCase))
            {
                var query = QueryHelpers.ParseQuery(request.RequestUri.Query);
                if (query.ContainsKey("Statuses")) return Json("""{"Invoices":[]}""");
                SalesInvoiceRequests++;
                var page = int.Parse(Assert.Single(query["page"])!);
                var pageSize = int.Parse(Assert.Single(query["pageSize"])!);
                RequestedPageSizes.Add(pageSize);
                InvoicePageRequests.Add(page);
                if (page == RateLimitedPage)
                {
                    var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                    response.Headers.Add("X-Rate-Limit-Problem", "day");
                    response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1));
                    return Task.FromResult(response);
                }

                var records = Enumerable.Range(1, InvoicePages * pageSize);
                if (ShiftInvoices) records = records.Where(index => index > 2);
                var invoices = string.Join(',', records.Skip((page - 1) * pageSize).Take(pageSize).Select(Invoice));
                return Json($$"""{"Invoices":[{{invoices}}]}""");
            }

            if (path.EndsWith("/Invoices/" + InvoiceId(1), StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            if (path.EndsWith("/Invoices/" + InvoiceId(2), StringComparison.Ordinal))
                return Json($$"""{"Invoices":[{{Invoice(2)}}]}""");
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
                "ContactGroups" => "ContactGroups",
                _ => "Records"
            };
            return Json($$"""{"{{root}}":[ ]}""");
        }

        private static Task<HttpResponseMessage> Json(string body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
    }

    private sealed class ReadOnlyXeroHandler(Guid tenantId, Guid accountId, Guid contactId, Guid itemId) : HttpMessageHandler
    {
        private readonly Guid archivedContactId = Guid.NewGuid();
        private readonly Guid contactGroupId = Guid.NewGuid();
        public int Revision { get; set; } = 1;
        public bool HasArchivedContact { get; set; }
        public bool HasContactGroup { get; set; }
        public bool ReturnNoRecords { get; set; }
        public bool ReturnDuplicateAccountCode { get; set; }
        public bool OmitItemStatus { get; set; }
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
            if (ReturnNoRecords) return EmptyCollectionFor(request.RequestUri.AbsolutePath);
            var path = request.RequestUri.AbsolutePath;
            if (HasContactGroup && path.EndsWith("/ContactGroups", StringComparison.Ordinal))
                return Json($$"""{"ContactGroups":[{"ContactGroupID":"{{contactGroupId}}","Name":"Preferred customers","Status":"ACTIVE","Contacts":[]}]}""");
            if (HasContactGroup && path.EndsWith($"/ContactGroups/{contactGroupId:D}", StringComparison.Ordinal))
                return Json($$"""{"ContactGroups":[{"ContactGroupID":"{{contactGroupId}}","Name":"Preferred customers","Status":"ACTIVE","Contacts":[{"ContactID":"{{contactId}}","Name":"Xero Customer"}]}]}""");
            if (path.EndsWith("/Accounts", StringComparison.Ordinal))
            {
                if (ReturnDuplicateAccountCode)
                    return Json($$"""{"Accounts":[{"AccountID":"{{accountId}}","Code":"12345678901234567890-A","Name":"Office expenses","Type":"EXPENSE","Status":"ACTIVE"},{"AccountID":"{{Guid.NewGuid()}}","Code":"12345678901234567890-B","Name":"Old office expenses","Type":"EXPENSE","Status":"ARCHIVED"}]}""");
                return Json($$"""{"Accounts":[{"AccountID":"{{accountId}}","Code":"6200","Name":"Office expenses","Type":"EXPENSE","Status":"ACTIVE"}]}""");
            }
            if (path.EndsWith("/Contacts", StringComparison.Ordinal))
            {
                var includeArchived = QueryHelpers.ParseQuery(request.RequestUri.Query)["includeArchived"] == "true";
                var archived = HasArchivedContact && includeArchived
                    ? $$""",{"ContactID":"{{archivedContactId}}","Name":"Archived Xero Customer","EmailAddress":"first@example.test","IsCustomer":true,"ContactStatus":"ARCHIVED"}"""
                    : "";
                return Json($$"""{"Contacts":[{"ContactID":"{{contactId}}","Name":"Xero Customer","EmailAddress":"{{(Revision == 1 ? "first" : "second")}}@example.test","IsCustomer":true,"ContactStatus":"ACTIVE","UpdatedDateUTC":"2026-10-19T00:0{{Revision}}:00Z"}{{archived}}]}""");
            }
            if (path.EndsWith("/Items", StringComparison.Ordinal))
            {
                var status = OmitItemStatus ? "" : "\"Status\":\"ACTIVE\",";
                return Json($$$"""{"Items":[{"ItemID":"{{{itemId}}}","Code":"CONSULT","Name":"Consulting",{{{status}}}"SalesDetails":{"UnitPrice":42}}]}""");
            }
            return EmptyCollectionFor(path);
        }

        public static Task<HttpResponseMessage> EmptyCollectionFor(string path)
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
                "ContactGroups" => "ContactGroups",
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
                if (TaxRateRequests == 1) return RateLimited();
                return Json("""{"TaxRates":[]}""");
            }
            return ReadOnlyXeroHandler.EmptyCollectionFor(path);
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
