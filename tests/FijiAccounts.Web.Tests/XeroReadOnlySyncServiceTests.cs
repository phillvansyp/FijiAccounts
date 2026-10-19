using System.Net;
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
}
