using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FijiAccounts.Web.Services;

public sealed record XeroConnectionStatus(
    bool IsConfigured,
    bool IsConnected,
    Guid? TenantId,
    string? TenantName,
    IReadOnlyList<XeroTenantCandidate> AvailableTenants,
    DateTimeOffset? LastSyncedAt,
    string? LastSyncError);

public sealed record XeroSyncFailure(string Dataset, string Reason);
public sealed record XeroSyncResult(
    int ImportedDatasets,
    int ImportedRecords,
    bool ReconciliationReset,
    IReadOnlyList<XeroSyncFailure> Failures);
public sealed record XeroSnapshotDownload(byte[] Content, string FileName);

public sealed class XeroConnectionService(
    ApplicationDbContext db,
    TenantAccessService access,
    XeroApiClient api,
    IImmutableDocumentStore storage,
    IDataProtectionProvider dataProtection,
    IOptions<XeroApiOptions> configured)
{
    private const int MaximumDatasetBytes = 25 * 1024 * 1024;
    private const int MaximumPages = 100;
    private const int ExpectedPageSize = 100;
    private readonly XeroApiOptions options = configured.Value;
    private readonly IDataProtector tokenProtector = dataProtection.CreateProtector("AccountIsland.Xero.Token.v1");
    private readonly ITimeLimitedDataProtector stateProtector = dataProtection
        .CreateProtector("AccountIsland.Xero.OAuthState.v1")
        .ToTimeLimitedDataProtector();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly DatasetDefinition[] Datasets =
    [
        new("organisation", "Organisation settings", "Organisation", "Organisations", false),
        new("accounts", "Chart of accounts", "Accounts", "Accounts", false),
        new("tax-rates", "Tax rates", "TaxRates", "TaxRates", false),
        new("currencies", "Currencies", "Currencies", "Currencies", false),
        new("tracking", "Tracking categories", "TrackingCategories", "TrackingCategories", false),
        new("contacts", "Contacts", "Contacts", "Contacts", true),
        new("items", "Inventory items", "Items", "Items", true),
        new("sales-invoices", "Sales invoices", "Invoices?where=Type%3D%3D%22ACCREC%22", "Invoices", true),
        new("bills", "Supplier bills", "Invoices?where=Type%3D%3D%22ACCPAY%22", "Invoices", true),
        new("sales-credits", "Sales credit notes", "CreditNotes?where=Type%3D%3D%22ACCRECCREDIT%22", "CreditNotes", true),
        new("supplier-credits", "Supplier credit notes", "CreditNotes?where=Type%3D%3D%22ACCPAYCREDIT%22", "CreditNotes", true),
        new("payments", "Payments", "Payments", "Payments", true),
        new("batch-payments", "Batch payments", "BatchPayments", "BatchPayments", true),
        new("prepayments", "Prepayments", "Prepayments", "Prepayments", true),
        new("overpayments", "Overpayments", "Overpayments", "Overpayments", true),
        new("bank-transactions", "Bank transactions", "BankTransactions", "BankTransactions", true),
        new("bank-transfers", "Bank transfers", "BankTransfers", "BankTransfers", false),
        new("manual-journals", "Manual journals", "ManualJournals", "ManualJournals", true),
        new("purchase-orders", "Purchase orders", "PurchaseOrders", "PurchaseOrders", true),
        new("quotes", "Quotes", "Quotes", "Quotes", true),
        new("repeating-invoices", "Repeating invoices", "RepeatingInvoices", "RepeatingInvoices", false),
        new("budgets", "Budgets", "Budgets", "Budgets", false),
        new("assets", "Fixed assets", "https://api.xero.com/assets.xro/1.0/Assets", "Assets", false),
        new("asset-types", "Fixed-asset types", "https://api.xero.com/assets.xro/1.0/AssetTypes", "AssetTypes", false),
        new("asset-settings", "Fixed-asset settings", "https://api.xero.com/assets.xro/1.0/Settings", "Settings", false)
    ];

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(options.ClientId) &&
        !string.IsNullOrWhiteSpace(options.ClientSecret) &&
        HasSafeRedirectUri(options.RedirectUri) &&
        !string.IsNullOrWhiteSpace(options.Scopes);

    public async Task<XeroConnectionStatus> GetStatusAsync(
        string userId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        await DemandAccessAsync(userId, organisationId);
        var connection = await db.XeroConnections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganisationId == organisationId && x.IsActive, cancellationToken);
        var candidates = connection is null
            ? []
            : DeserializeCandidates(connection.TenantCandidatesJson);
        return new(
            IsConfigured,
            connection is not null,
            connection?.XeroTenantId,
            connection?.XeroTenantName,
            candidates,
            connection?.LastSyncedAt,
            connection?.LastSyncError);
    }

    public async Task<string> CreateAuthorizationUriAsync(
        string userId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        await DemandManageAsync(userId, organisationId);
        if (!IsConfigured)
        {
            throw new InvalidOperationException("The Xero connection is waiting for its app credentials in deployment settings.");
        }
        if (!await db.XeroMigrations.AnyAsync(x => x.OrganisationId == organisationId, cancellationToken))
        {
            throw new InvalidOperationException("Save the migration setup before connecting Xero.");
        }
        var state = stateProtector.Protect(
            JsonSerializer.Serialize(new OAuthState(userId, organisationId, Guid.NewGuid())),
            TimeSpan.FromMinutes(15));
        return QueryHelpers.AddQueryString(
            "https://login.xero.com/identity/connect/authorize",
            new Dictionary<string, string?>
            {
                ["response_type"] = "code",
                ["client_id"] = options.ClientId,
                ["redirect_uri"] = options.RedirectUri,
                ["scope"] = options.Scopes,
                ["state"] = state,
                ["prompt"] = "select_account"
            });
    }

    public async Task<Guid> CompleteAuthorizationAsync(
        string userId,
        string code,
        string protectedState,
        CancellationToken cancellationToken = default)
    {
        OAuthState state;
        try
        {
            state = JsonSerializer.Deserialize<OAuthState>(stateProtector.Unprotect(protectedState), JsonOptions)
                ?? throw new CryptographicException();
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            throw new InvalidOperationException("The Xero connection request expired. Start the connection again.");
        }
        if (!string.Equals(state.UserId, userId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("The Xero connection must be completed by the person who started it.");
        }
        await DemandManageAsync(userId, state.OrganisationId);
        var token = await api.ExchangeCodeAsync(code, cancellationToken);
        var tenants = await api.GetConnectionsAsync(token.AccessToken, cancellationToken);
        if (tenants.Count == 0)
        {
            throw new InvalidOperationException("Xero did not return an organisation. Confirm access in Xero and reconnect.");
        }

        var connection = await db.XeroConnections.SingleOrDefaultAsync(
            x => x.OrganisationId == state.OrganisationId, cancellationToken);
        var selected = tenants.Count == 1
            ? tenants[0]
            : tenants.SingleOrDefault(x => x.TenantId == connection?.XeroTenantId);
        connection ??= new XeroConnection
        {
            OrganisationId = state.OrganisationId,
            ProtectedAccessToken = string.Empty,
            ProtectedRefreshToken = string.Empty,
            GrantedScopes = string.Empty,
            ConnectedByUserId = userId,
            UpdatedByUserId = userId
        };
        if (db.Entry(connection).State == EntityState.Detached) db.XeroConnections.Add(connection);
        connection.ProtectedAccessToken = tokenProtector.Protect(token.AccessToken);
        connection.ProtectedRefreshToken = tokenProtector.Protect(token.RefreshToken);
        connection.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
        connection.GrantedScopes = token.Scope;
        connection.TenantCandidatesJson = JsonSerializer.Serialize(tenants, JsonOptions);
        connection.XeroConnectionId = selected?.ConnectionId;
        connection.XeroTenantId = selected?.TenantId;
        connection.XeroTenantName = selected?.TenantName;
        connection.IsActive = true;
        connection.UpdatedAt = DateTimeOffset.UtcNow;
        connection.UpdatedByUserId = userId;
        connection.LastSyncError = null;
        db.AuditEvents.Add(Audit(state.OrganisationId, userId, "XeroConnected", connection.Id, new
        {
            TenantCount = tenants.Count,
            SelectedTenantId = selected?.TenantId,
            SelectedTenantName = selected?.TenantName,
            token.Scope
        }));
        await db.SaveChangesAsync(cancellationToken);
        return state.OrganisationId;
    }

    public async Task SelectTenantAsync(
        string userId,
        Guid organisationId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        await DemandManageAsync(userId, organisationId);
        var connection = await db.XeroConnections.SingleOrDefaultAsync(
            x => x.OrganisationId == organisationId && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("Connect Xero first.");
        var selected = DeserializeCandidates(connection.TenantCandidatesJson)
            .SingleOrDefault(x => x.TenantId == tenantId)
            ?? throw new InvalidOperationException("Choose an organisation returned by Xero.");
        if (connection.XeroTenantId != tenantId && await db.XeroApiSnapshots.AnyAsync(
                x => x.OrganisationId == organisationId, cancellationToken))
        {
            throw new InvalidOperationException(
                "This migration already contains Xero snapshots. Keep the same Xero organisation so records cannot be mixed.");
        }
        connection.XeroTenantId = selected.TenantId;
        connection.XeroTenantName = selected.TenantName;
        connection.XeroConnectionId = selected.ConnectionId;
        connection.UpdatedAt = DateTimeOffset.UtcNow;
        connection.UpdatedByUserId = userId;
        db.AuditEvents.Add(Audit(organisationId, userId, "XeroTenantSelected", connection.Id, new
        {
            selected.TenantId,
            selected.TenantName
        }));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<XeroSyncResult> SyncAsync(
        string userId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException("You cannot import Xero data for this organisation.");
        }
        var migration = await db.XeroMigrations.SingleOrDefaultAsync(
            x => x.OrganisationId == organisationId, cancellationToken)
            ?? throw new InvalidOperationException("Save the migration setup first.");
        var connection = await db.XeroConnections.SingleOrDefaultAsync(
            x => x.OrganisationId == organisationId && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("Connect Xero first.");
        if (connection.XeroTenantId is null)
        {
            throw new InvalidOperationException("Choose which Xero organisation to import.");
        }
        var accessToken = await GetAccessTokenAsync(connection, userId, cancellationToken);
        var definitions = Datasets.AsEnumerable();
        if (options.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains("accounting.journals.read", StringComparer.OrdinalIgnoreCase))
        {
            definitions = definitions.Append(new DatasetDefinition(
                "journals", "Posted journals", "Journals", "Journals", true));
        }
        var definitionsArray = definitions.Concat(ReportDefinitions(migration.CutoverDate)).ToArray();
        var failures = new List<XeroSyncFailure>();
        var importedRecords = 0;
        var importedDatasets = 0;

        foreach (var definition in definitionsArray)
        {
            try
            {
                var fetched = await FetchDatasetAsync(accessToken, connection.XeroTenantId.Value, definition, cancellationToken);
                var stored = storage.Stage(organisationId, userId, fetched.Content);
                db.XeroApiSnapshots.Add(new XeroApiSnapshot
                {
                    OrganisationId = organisationId,
                    XeroMigrationId = migration.Id,
                    DatasetKey = definition.Key,
                    DisplayName = definition.Name,
                    Endpoint = definition.Path,
                    RecordCount = fetched.RecordCount,
                    PageCount = fetched.PageCount,
                    Size = fetched.Content.LongLength,
                    Sha256 = stored.Sha256,
                    ImmutableDocumentObjectId = stored.Id,
                    RetrievedByUserId = userId
                });
                importedDatasets++;
                importedRecords += fetched.RecordCount;
            }
            catch (Exception ex) when (ex is XeroApiException or JsonException or InvalidOperationException)
            {
                failures.Add(new(definition.Name, FriendlyFailure(ex)));
            }
        }

        connection.LastSyncedAt = DateTimeOffset.UtcNow;
        var failureSummary = string.Join("; ", failures.Select(x => $"{x.Dataset}: {x.Reason}"));
        connection.LastSyncError = failures.Count == 0
            ? null
            : failureSummary[..Math.Min(2000, failureSummary.Length)];
        connection.UpdatedAt = DateTimeOffset.UtcNow;
        connection.UpdatedByUserId = userId;
        var reconciliationReset = importedDatasets > 0 &&
            (migration.ManagementApproved || migration.TrialBalanceReconciled || migration.ReceivablesReconciled ||
             migration.PayablesReconciled || migration.BankReconciled || migration.FixedAssetsReconciled ||
             migration.GstReconciled);
        if (importedDatasets > 0)
        {
            migration.ManagementApproved = false;
            migration.TrialBalanceReconciled = false;
            migration.ReceivablesReconciled = false;
            migration.PayablesReconciled = false;
            migration.BankReconciled = false;
            migration.FixedAssetsReconciled = false;
            migration.GstReconciled = false;
            migration.Status = XeroMigrationStatus.Preparing;
            migration.UpdatedAt = DateTimeOffset.UtcNow;
            migration.UpdatedByUserId = userId;
        }
        db.AuditEvents.Add(Audit(organisationId, userId, "XeroApiSnapshotImported", migration.Id, new
        {
            connection.XeroTenantId,
            connection.XeroTenantName,
            ImportedDatasets = importedDatasets,
            ImportedRecords = importedRecords,
            ReconciliationReset = reconciliationReset,
            FailedDatasets = failures.Select(x => x.Dataset).ToArray()
        }, nameof(XeroMigration)));
        await db.SaveChangesAsync(cancellationToken);
        return new(importedDatasets, importedRecords, reconciliationReset, failures);
    }

    public async Task<XeroSnapshotDownload?> ReadSnapshotAsync(
        string userId,
        Guid organisationId,
        Guid snapshotId,
        CancellationToken cancellationToken = default)
    {
        await DemandAccessAsync(userId, organisationId);
        var snapshot = await db.XeroApiSnapshots.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == snapshotId && x.OrganisationId == organisationId, cancellationToken);
        if (snapshot is null) return null;
        var content = await storage.ReadVerifiedAsync(organisationId, snapshot.ImmutableDocumentObjectId, cancellationToken);
        db.AuditEvents.Add(Audit(organisationId, userId, "XeroApiSnapshotOpened", snapshot.Id, new
        {
            snapshot.DatasetKey,
            snapshot.DisplayName,
            snapshot.Sha256
        }, nameof(XeroApiSnapshot)));
        await db.SaveChangesAsync(cancellationToken);
        var timestamp = snapshot.RetrievedAt.UtcDateTime.ToString("yyyyMMdd-HHmmss");
        return new(content, $"xero-{snapshot.DatasetKey}-{timestamp}.json");
    }

    private async Task<string> GetAccessTokenAsync(
        XeroConnection connection,
        string userId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (connection.AccessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
            {
                return tokenProtector.Unprotect(connection.ProtectedAccessToken);
            }
            var refreshed = await api.RefreshAsync(
                tokenProtector.Unprotect(connection.ProtectedRefreshToken), cancellationToken);
            connection.ProtectedAccessToken = tokenProtector.Protect(refreshed.AccessToken);
            connection.ProtectedRefreshToken = tokenProtector.Protect(refreshed.RefreshToken);
            connection.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(refreshed.ExpiresIn);
            connection.GrantedScopes = refreshed.Scope;
            connection.UpdatedAt = DateTimeOffset.UtcNow;
            connection.UpdatedByUserId = userId;
            return refreshed.AccessToken;
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("The saved Xero credentials cannot be opened. Reconnect Xero.", ex);
        }
    }

    private async Task<FetchedDataset> FetchDatasetAsync(
        string accessToken,
        Guid tenantId,
        DatasetDefinition definition,
        CancellationToken cancellationToken)
    {
        var pages = new List<JsonDocument>();
        var total = 0;
        try
        {
            for (var page = 1; page <= (definition.Paged ? MaximumPages : 1); page++)
            {
                var endpoint = definition.Paged ? AddPage(definition.Path, page) : definition.Path;
                var bytes = await api.GetJsonAsync(accessToken, tenantId, endpoint, cancellationToken);
                var document = JsonDocument.Parse(bytes);
                var count = CountRecords(document.RootElement, definition.RootProperty);
                pages.Add(document);
                total += count;
                if (!definition.Paged || count < ExpectedPageSize) break;
                if (page == MaximumPages)
                {
                    throw new InvalidOperationException($"{definition.Name} exceeds the safe import limit. Export the remaining history from Xero.");
                }
            }

            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject();
                writer.WriteString("dataset", definition.Key);
                writer.WriteString("source", "Xero API");
                writer.WriteString("retrievedAt", DateTimeOffset.UtcNow);
                writer.WriteNumber("recordCount", total);
                writer.WritePropertyName("pages");
                writer.WriteStartArray();
                foreach (var page in pages) page.RootElement.WriteTo(writer);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            if (output.Length > MaximumDatasetBytes)
            {
                throw new InvalidOperationException($"{definition.Name} is larger than 25 MB. Use the Xero export upload for this dataset.");
            }
            return new(output.ToArray(), total, pages.Count);
        }
        finally
        {
            foreach (var page in pages) page.Dispose();
        }
    }

    private static int CountRecords(JsonElement root, string property)
    {
        if (root.ValueKind == JsonValueKind.Array) return root.GetArrayLength();
        foreach (var item in root.EnumerateObject())
        {
            if (item.Name.Equals(property, StringComparison.OrdinalIgnoreCase) && item.Value.ValueKind == JsonValueKind.Array)
                return item.Value.GetArrayLength();
        }
        return root.ValueKind == JsonValueKind.Object ? 1 : 0;
    }

    private static string AddPage(string path, int page) =>
        path + (path.Contains('?') ? "&" : "?") + $"page={page}";

    private static IEnumerable<DatasetDefinition> ReportDefinitions(DateOnly? cutoverDate)
    {
        var end = (cutoverDate ?? DateOnly.FromDateTime(DateTime.UtcNow)).AddDays(-1);
        var start = end.AddYears(-1).AddDays(1);
        var encodedEnd = end.ToString("yyyy-MM-dd");
        return
        [
            new("trial-balance", "Trial Balance control report", $"Reports/TrialBalance?date={encodedEnd}", "Reports", false),
            new("balance-sheet", "Balance Sheet control report", $"Reports/BalanceSheet?date={encodedEnd}", "Reports", false),
            new("profit-loss", "Profit and Loss control report", $"Reports/ProfitAndLoss?fromDate={start:yyyy-MM-dd}&toDate={encodedEnd}", "Reports", false),
            new("aged-receivables", "Aged receivables control report", $"Reports/AgedReceivablesByContact?date={encodedEnd}", "Reports", false),
            new("aged-payables", "Aged payables control report", $"Reports/AgedPayablesByContact?date={encodedEnd}", "Reports", false)
        ];
    }

    private static string FriendlyFailure(Exception exception) => exception switch
    {
        XeroApiException x when x.StatusCode == System.Net.HttpStatusCode.Unauthorized =>
            "Xero permission is missing. Reconnect and approve the requested read-only access.",
        XeroApiException x when x.StatusCode == System.Net.HttpStatusCode.Forbidden =>
            "This Xero plan or user role does not allow the dataset.",
        XeroApiException x when x.StatusCode == System.Net.HttpStatusCode.NotFound =>
            "This dataset is not available for the connected organisation.",
        XeroApiException x => x.Message,
        _ => exception.Message
    };

    private async Task DemandAccessAsync(string userId, Guid organisationId)
    {
        if (await access.FindAsync(userId, organisationId) is null) throw new UnauthorizedAccessException();
    }

    private async Task DemandManageAsync(string userId, Guid organisationId)
    {
        if (!await access.CanManageTeamAsync(userId, organisationId))
            throw new UnauthorizedAccessException("Only an owner or administrator can connect Xero.");
    }

    private static IReadOnlyList<XeroTenantCandidate> DeserializeCandidates(string json)
    {
        try { return JsonSerializer.Deserialize<List<XeroTenantCandidate>>(json, JsonOptions) ?? []; }
        catch (JsonException) { return []; }
    }

    private static bool HasSafeRedirectUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
               (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.IsLoopback));
    }

    private static AuditEvent Audit(
        Guid organisationId,
        string userId,
        string eventType,
        Guid entityId,
        object evidence,
        string entityType = nameof(XeroConnection)) => new()
    {
        OrganisationId = organisationId,
        UserId = userId,
        EventType = eventType,
        EntityType = entityType,
        EntityId = entityId.ToString(),
        JsonData = JsonSerializer.Serialize(evidence)
    };

    private sealed record OAuthState(string UserId, Guid OrganisationId, Guid Nonce);
    private sealed record DatasetDefinition(string Key, string Name, string Path, string RootProperty, bool Paged);
    private sealed record FetchedDataset(byte[] Content, int RecordCount, int PageCount);
}
