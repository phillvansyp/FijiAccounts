using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
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
    string? LastSyncError,
    bool ContinuousSyncEnabled = false,
    DateTimeOffset? LastFullReconciliationAt = null,
    DateTimeOffset? NextSyncAt = null,
    bool NeedsPermissionUpdate = false);

public sealed record XeroSyncFailure(string Dataset, string Reason);
public sealed record XeroSyncResult(
    int ImportedDatasets,
    int ImportedRecords,
    bool ReconciliationReset,
    IReadOnlyList<XeroSyncFailure> Failures);
public sealed record XeroSnapshotDownload(byte[] Content, string FileName);

public sealed class XeroReconnectRequiredException(Exception innerException) : InvalidOperationException(
    "Xero no longer accepts the saved authorisation. Reconnect Xero to resume automatic sync; records already copied into Account Island are safe.",
    innerException);

public sealed class XeroConnectionService(
    ApplicationDbContext db,
    TenantAccessService access,
    XeroApiClient api,
    IImmutableDocumentStore storage,
    IDataProtectionProvider dataProtection,
    IOptions<XeroApiOptions> configured)
{
    private const int MaximumDatasetBytes = 25 * 1024 * 1024;
    private const int MaximumAttachmentPackageBytes = 25 * 1024 * 1024;
    private const int AttachmentPackageTargetBytes = 20 * 1024 * 1024;
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
            connection?.LastSyncError,
            connection?.ContinuousSyncEnabled ?? false,
            connection?.LastFullReconciliationAt,
            connection?.NextSyncAt,
            connection is not null && HasMissingScopes(connection.GrantedScopes));
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
        var resumeAutomaticSync = connection?.LastSyncError?.Contains(
            "Reconnect Xero to resume automatic sync", StringComparison.OrdinalIgnoreCase) == true;
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
        if (resumeAutomaticSync)
        {
            connection.ContinuousSyncEnabled = true;
            connection.NextSyncAt = DateTimeOffset.UtcNow;
        }
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
        var migration = await db.XeroMigrations.AsNoTracking().SingleOrDefaultAsync(
            x => x.OrganisationId == organisationId, cancellationToken);
        var attemptStartedAt = migration is null ? null : await XeroMigrationAttempt.StartedAtAsync(
            db, organisationId, migration.Id, cancellationToken);
        var currentSnapshotsExist = (await db.XeroApiSnapshots.AsNoTracking()
                .Where(x => x.OrganisationId == organisationId)
                .Select(x => x.RetrievedAt)
                .ToListAsync(cancellationToken))
            .Any(x => XeroMigrationAttempt.IsCurrent(x, attemptStartedAt));
        if (connection.XeroTenantId != tenantId && currentSnapshotsExist)
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
        var attemptStartedAt = await XeroMigrationAttempt.StartedAtAsync(
            db, organisationId, migration.Id, cancellationToken);
        var connection = await db.XeroConnections.SingleOrDefaultAsync(
            x => x.OrganisationId == organisationId && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("Connect Xero first.");
        if (connection.XeroTenantId is null)
        {
            throw new InvalidOperationException("Choose which Xero organisation to import.");
        }
        var leaseStartedAt = DateTimeOffset.UtcNow;
        var previousLease = connection.SyncLeaseUntil;
        if (previousLease > leaseStartedAt)
            throw new InvalidOperationException("A Xero refresh is already running. Wait for it to finish, then try again.");
        var leaseUntil = leaseStartedAt.AddMinutes(30);
        var claimed = await db.XeroConnections
            .Where(x => x.Id == connection.Id && x.SyncLeaseUntil == previousLease)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.SyncLeaseUntil, leaseUntil), cancellationToken);
        if (claimed == 0)
            throw new InvalidOperationException("A Xero refresh is already running. Wait for it to finish, then try again.");
        connection.SyncLeaseUntil = leaseUntil;

        try
        {
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
        var attachmentSources = new List<AttachmentSource>();
        var failedAttachmentDatasets = new List<string>();
        XeroApiException? rateLimitFailure = null;

        foreach (var definition in definitionsArray)
        {
            try
            {
                var fetched = await FetchDatasetAsync(accessToken, connection.XeroTenantId.Value, definition, cancellationToken);
                var datasetAttachmentSources = ExtractAttachmentSources(fetched.Content, definition);
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
                // Each completed dataset is durable immediately. A later Xero
                // timeout or attachment problem cannot erase earlier progress.
                await db.SaveChangesAsync(cancellationToken);
                importedDatasets++;
                importedRecords += fetched.RecordCount;
                attachmentSources.AddRange(datasetAttachmentSources);
            }
            catch (Exception ex) when (ex is XeroApiException or JsonException or InvalidOperationException)
            {
                failures.Add(new(definition.Name, FriendlyFailure(ex)));
                if (AttachmentDefinitions.ContainsKey(definition.Key))
                    failedAttachmentDatasets.Add(definition.Name);
                if (ex is XeroApiException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } rateLimit)
                {
                    rateLimitFailure = rateLimit;
                    break;
                }
            }
        }

        // Persist any refreshed OAuth token even when every source dataset was
        // unavailable. Completed datasets were saved individually above.
        await db.SaveChangesAsync(cancellationToken);

        if (rateLimitFailure is null)
        {
            try
            {
                if (failedAttachmentDatasets.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"Attachments cannot be verified because these Xero records were not copied: {string.Join(", ", failedAttachmentDatasets)}.");
                }
                var packages = await FetchAttachmentPackagesAsync(
                    accessToken,
                    connection.XeroTenantId.Value,
                    attachmentSources,
                    cancellationToken);
                foreach (var package in packages)
                {
                    var matchingFiles = await db.XeroMigrationFiles.AsNoTracking().Where(
                            x => x.XeroMigrationId == migration.Id &&
                                 x.Category == "Document attachments" &&
                                 x.Sha256 == package.Sha256)
                        .Select(x => x.UploadedAt)
                        .ToListAsync(cancellationToken);
                    if (matchingFiles.Any(x => XeroMigrationAttempt.IsCurrent(x, attemptStartedAt))) continue;

                    var stored = storage.Stage(organisationId, userId, package.Content);
                    var file = new XeroMigrationFile
                    {
                        OrganisationId = organisationId,
                        XeroMigrationId = migration.Id,
                        Category = "Document attachments",
                        FileName = package.FileName,
                        ContentType = "application/zip",
                        Size = package.Content.LongLength,
                        Sha256 = stored.Sha256,
                        ImmutableDocumentObjectId = stored.Id,
                        UploadedByUserId = userId
                    };
                    db.XeroMigrationFiles.Add(file);
                    db.AuditEvents.Add(Audit(organisationId, userId, "XeroAttachmentPackageRetained", file.Id, new
                    {
                        migration.Id,
                        file.FileName,
                        file.Size,
                        file.Sha256,
                        package.AttachmentCount
                    }, nameof(XeroMigrationFile)));
                }
            }
            catch (Exception ex) when (ex is XeroApiException or JsonException or InvalidOperationException)
            {
                failures.Add(new("Document attachments", FriendlyFailure(ex)));
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
        finally
        {
            // The migration copy and the scheduled read-only mirror share this
            // database lease. Always release it, including after an unexpected
            // source shape or a cancelled browser operation.
            await db.XeroConnections
                .Where(x => x.Id == connection.Id && x.SyncLeaseUntil == leaseUntil)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.SyncLeaseUntil, (DateTimeOffset?)null)
                    .SetProperty(x => x.SyncLeaseOwner, (string?)null), CancellationToken.None);
            var trackedLease = db.Entry(connection).Property(x => x.SyncLeaseUntil);
            trackedLease.CurrentValue = null;
            trackedLease.OriginalValue = null;
            trackedLease.IsModified = false;
            var trackedLeaseOwner = db.Entry(connection).Property(x => x.SyncLeaseOwner);
            trackedLeaseOwner.CurrentValue = null;
            trackedLeaseOwner.OriginalValue = null;
            trackedLeaseOwner.IsModified = false;
        }
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

    internal async Task<string> GetAccessTokenAsync(
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
        catch (XeroApiException ex) when (ex.StatusCode is
            System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized)
        {
            throw new XeroReconnectRequiredException(ex);
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
            new("profit-loss", "Profit and Loss control report", $"Reports/ProfitAndLoss?fromDate={start:yyyy-MM-dd}&toDate={encodedEnd}", "Reports", false)
        ];
    }

    private static IReadOnlyList<AttachmentSource> ExtractAttachmentSources(
        byte[] content,
        DatasetDefinition definition)
    {
        if (!AttachmentDefinitions.TryGetValue(definition.Key, out var attachmentDefinition)) return [];
        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array)
            return [];

        var sources = new List<AttachmentSource>();
        foreach (var page in pages.EnumerateArray())
        {
            if (!TryGetPropertyIgnoreCase(page, definition.RootProperty, out var records) ||
                records.ValueKind != JsonValueKind.Array) continue;
            foreach (var record in records.EnumerateArray())
            {
                if (!TryGetPropertyIgnoreCase(record, "HasAttachments", out var hasAttachments) ||
                    hasAttachments.ValueKind != JsonValueKind.True ||
                    !TryGetPropertyIgnoreCase(record, attachmentDefinition.IdProperty, out var idValue) ||
                    !Guid.TryParse(idValue.GetString(), out var recordId)) continue;
                sources.Add(new(
                    attachmentDefinition.EntityPath,
                    attachmentDefinition.RecordType,
                    recordId));
            }
        }
        return sources;
    }

    private async Task<IReadOnlyList<AttachmentPackage>> FetchAttachmentPackagesAsync(
        string accessToken,
        Guid tenantId,
        IReadOnlyCollection<AttachmentSource> sources,
        CancellationToken cancellationToken)
    {
        var attachments = new List<DownloadedAttachment>();
        foreach (var source in sources
                     .Distinct()
                     .OrderBy(x => x.RecordType, StringComparer.Ordinal)
                     .ThenBy(x => x.RecordId))
        {
            var listEndpoint = $"{source.EntityPath}/{source.RecordId:D}/Attachments";
            var metadataBytes = await api.GetJsonAsync(accessToken, tenantId, listEndpoint, cancellationToken);
            using var metadata = JsonDocument.Parse(metadataBytes);
            if (!TryGetPropertyIgnoreCase(metadata.RootElement, "Attachments", out var list) ||
                list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
            {
                throw new InvalidOperationException(
                    $"Xero reported attachments for {source.RecordType} {source.RecordId:D}, but did not return their file details.");
            }

            foreach (var item in list.EnumerateArray())
            {
                if (!TryGetPropertyIgnoreCase(item, "AttachmentID", out var idValue) ||
                    !Guid.TryParse(idValue.GetString(), out var attachmentId))
                    throw new InvalidOperationException($"Xero returned an attachment without an identifier for {source.RecordType} {source.RecordId:D}.");
                var fileName = TryGetString(item, "FileName") ?? $"attachment-{attachmentId:N}";
                var mimeType = TryGetString(item, "MimeType") ?? "application/octet-stream";
                var endpoint = $"{source.EntityPath}/{source.RecordId:D}/Attachments/{attachmentId:D}";
                var bytes = await api.GetBytesAsync(accessToken, tenantId, endpoint, mimeType, cancellationToken);
                if (bytes.Length == 0)
                    throw new InvalidOperationException($"Xero returned an empty attachment for {source.RecordType} {source.RecordId:D}.");
                attachments.Add(new(source, attachmentId, fileName, mimeType, bytes));
            }
        }

        return BuildAttachmentPackages(attachments);
    }

    private static IReadOnlyList<AttachmentPackage> BuildAttachmentPackages(
        IReadOnlyCollection<DownloadedAttachment> attachments)
    {
        var ordered = attachments
            .OrderBy(x => x.Source.RecordType, StringComparer.Ordinal)
            .ThenBy(x => x.Source.RecordId)
            .ThenBy(x => x.AttachmentId)
            .ToArray();
        var groups = new List<List<DownloadedAttachment>>();
        var current = new List<DownloadedAttachment>();
        var currentBytes = 0L;
        foreach (var attachment in ordered)
        {
            if (current.Count > 0 && currentBytes + attachment.Content.LongLength > AttachmentPackageTargetBytes)
            {
                groups.Add(current);
                current = [];
                currentBytes = 0;
            }
            current.Add(attachment);
            currentBytes += attachment.Content.LongLength;
        }
        if (current.Count > 0 || groups.Count == 0) groups.Add(current);

        var packages = new List<AttachmentPackage>();
        for (var index = 0; index < groups.Count; index++)
        {
            var content = BuildAttachmentZip(groups[index]);
            if (content.LongLength > MaximumAttachmentPackageBytes)
            {
                throw new InvalidOperationException(
                    $"A Xero document package is larger than 25 MB. Download the affected files from Xero and upload them as smaller ZIP files.");
            }
            var hash = Convert.ToHexString(SHA256.HashData(content));
            packages.Add(new(
                $"xero-document-attachments-{index + 1:D3}-{hash[..12].ToLowerInvariant()}.zip",
                content,
                hash,
                groups[index].Count));
        }
        return packages;
    }

    private static byte[] BuildAttachmentZip(IReadOnlyCollection<DownloadedAttachment> attachments)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            var manifest = attachments.Select(x => new
            {
                x.Source.RecordType,
                RecordId = x.Source.RecordId,
                AttachmentId = x.AttachmentId,
                x.FileName,
                x.MimeType,
                Size = x.Content.LongLength,
                Sha256 = Convert.ToHexString(SHA256.HashData(x.Content))
            }).ToArray();
            WriteZipEntry(archive, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
            foreach (var attachment in attachments)
            {
                var safeName = SafeEntryName(attachment.FileName);
                var entryName = $"records/{attachment.Source.RecordType}/{attachment.Source.RecordId:D}/{attachment.AttachmentId:D}-{safeName}";
                WriteZipEntry(archive, entryName, attachment.Content);
            }
        }
        return output.ToArray();
    }

    private static void WriteZipEntry(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static string SafeEntryName(string value)
    {
        var name = Path.GetFileName(value);
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\']).ToHashSet();
        var safe = new string(name.Select(x => invalid.Contains(x) ? '_' : x).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "attachment" : safe[..Math.Min(120, safe.Length)];
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private static string? TryGetString(JsonElement element, string name) =>
        TryGetPropertyIgnoreCase(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string FriendlyFailure(Exception exception) => exception switch
    {
        XeroApiException x when x.StatusCode == System.Net.HttpStatusCode.TooManyRequests => RateLimitMessage(x),
        XeroApiException x when x.StatusCode == System.Net.HttpStatusCode.Unauthorized =>
            "Xero permission is missing. Reconnect and approve the requested read-only access.",
        XeroApiException x when x.StatusCode == System.Net.HttpStatusCode.Forbidden =>
            "This Xero plan or user role does not allow the dataset.",
        XeroApiException x when x.StatusCode == System.Net.HttpStatusCode.NotFound =>
            "This dataset is not available for the connected organisation.",
        XeroApiException x => x.Message,
        _ => exception.Message
    };

    private bool HasMissingScopes(string grantedScopes)
    {
        var granted = grantedScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var requested in options.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!requested.StartsWith("accounting.", StringComparison.OrdinalIgnoreCase) &&
                !requested.StartsWith("assets.", StringComparison.OrdinalIgnoreCase)) continue;
            if (granted.Contains(requested)) continue;
            var coveredByBroadTransactionScope =
                (requested is "accounting.invoices.read" or "accounting.payments.read" or
                    "accounting.banktransactions.read" or "accounting.manualjournals.read") &&
                (granted.Contains("accounting.transactions.read") || granted.Contains("accounting.transactions"));
            if (coveredByBroadTransactionScope) continue;
            if (requested.StartsWith("accounting.reports.", StringComparison.OrdinalIgnoreCase) &&
                granted.Contains("accounting.reports.read")) continue;
            return true;
        }
        return false;
    }

    internal static string RateLimitMessage(XeroApiException exception)
    {
        var limit = exception.RateLimitProblem?.Equals("day", StringComparison.OrdinalIgnoreCase) == true
            ? "daily"
            : "request";
        if (exception.RetryAfter is not { } delay || delay <= TimeSpan.Zero)
            return $"Xero's {limit} limit was reached. Wait for Xero to reset it before trying again.";
        var minutes = Math.Max(1, (int)Math.Ceiling(delay.TotalMinutes));
        return $"Xero's {limit} limit was reached. Sync paused for about {minutes:N0} minute(s) before another attempt.";
    }

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
    private sealed record AttachmentDefinition(string EntityPath, string RecordType, string IdProperty);
    private sealed record AttachmentSource(string EntityPath, string RecordType, Guid RecordId);
    private sealed record DownloadedAttachment(
        AttachmentSource Source,
        Guid AttachmentId,
        string FileName,
        string MimeType,
        byte[] Content);
    private sealed record AttachmentPackage(string FileName, byte[] Content, string Sha256, int AttachmentCount);

    private static readonly IReadOnlyDictionary<string, AttachmentDefinition> AttachmentDefinitions =
        new Dictionary<string, AttachmentDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["accounts"] = new("Accounts", "Account", "AccountID"),
            ["contacts"] = new("Contacts", "Contact", "ContactID"),
            ["sales-invoices"] = new("Invoices", "Invoice", "InvoiceID"),
            ["bills"] = new("Invoices", "Invoice", "InvoiceID"),
            ["sales-credits"] = new("CreditNotes", "CreditNote", "CreditNoteID"),
            ["supplier-credits"] = new("CreditNotes", "CreditNote", "CreditNoteID"),
            ["bank-transactions"] = new("BankTransactions", "BankTransaction", "BankTransactionID"),
            ["bank-transfers"] = new("BankTransfers", "BankTransfer", "BankTransferID"),
            ["manual-journals"] = new("ManualJournals", "ManualJournal", "ManualJournalID"),
            ["purchase-orders"] = new("PurchaseOrders", "PurchaseOrder", "PurchaseOrderID"),
            ["quotes"] = new("Quotes", "Quote", "QuoteID"),
            ["repeating-invoices"] = new("RepeatingInvoices", "RepeatingInvoice", "RepeatingInvoiceID")
        };
}
