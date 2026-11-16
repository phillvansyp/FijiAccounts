using System.Net;
using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FijiAccounts.Web.Services;

public sealed record XeroReportRow(string Kind, string? Title, IReadOnlyList<string> Cells, int Depth);

public sealed record XeroReportResult(
    string? Organisation, Uri Source, string? TaxBasis, string? Title,
    IReadOnlyList<string> Subtitles, IReadOnlyList<XeroReportRow> Rows,
    DateTimeOffset? RetrievedAt, string? Message);

/// <summary>Reads report figures from the connected Xero organisation without posting to either ledger.</summary>
public sealed class XeroReportingService(
    ApplicationDbContext db, TenantAccessService access, XeroConnectionService connections,
    XeroApiClient api, XeroOpenBalanceService openBalances, IMemoryCache cache, ILogger<XeroReportingService> logger)
{
    private const string ReportsPage = "https://reporting.xero.com";
    private static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RetainFor = TimeSpan.FromHours(24);

    public async Task<bool> UsesXeroAsync(Guid organisationId, CancellationToken ct = default) =>
        await db.XeroConnections.AsNoTracking().AnyAsync(x => x.OrganisationId == organisationId &&
            x.IsActive && x.ContinuousSyncEnabled && x.XeroTenantId != null &&
            !db.XeroMigrations.Any(m => m.OrganisationId == organisationId && m.Status == XeroMigrationStatus.Complete), ct);

    public async Task<XeroReportResult> ReadAsync(
        string userId, Guid organisationId, string kind, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (await access.GetReportDivisionScopeAsync(userId, organisationId, ct) is not null)
            return Empty(null, new Uri(ReportsPage), null, "This Xero report requires organisation-wide reporting access.");

        var connection = await db.XeroConnections.AsNoTracking().SingleOrDefaultAsync(x =>
            x.OrganisationId == organisationId && x.IsActive && x.ContinuousSyncEnabled &&
            !db.XeroMigrations.Any(m => m.OrganisationId == organisationId && m.Status == XeroMigrationStatus.Complete), ct);
        if (connection?.XeroTenantId is not Guid tenantId)
            return Empty(null, new Uri(ReportsPage), null, "An active Xero connection is required.");

        var now = DateTimeOffset.UtcNow;
        var previousLease = connection.SyncLeaseUntil;
        var leaseOwner = $"xero-report:{Guid.NewGuid():N}";
        var identityKey = (connection.Id, tenantId, "xero-report-identity");
        cache.TryGetValue<OrganisationInfo>(identityKey, out var identity);
        var endpoint = Endpoint(kind, from, to);
        var reportKey = (connection.Id, tenantId, "xero-report",
            kind == "gst" ? $"gst:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}" : endpoint ?? kind);
        cache.TryGetValue<XeroReportResult>(reportKey, out var saved);
        if (saved?.RetrievedAt is { } retrieved && now - retrieved < FreshFor)
            return saved;

        var source = SourceLink(identity, kind, from, to);
        if ((kind is "receivables" or "payables") && to == DateOnly.FromDateTime(DateTime.Today))
        {
            try
            {
                var balances = await openBalances.ReadAsync(organisationId, kind == "payables", ct);
                var rows = RenderAgeing(balances, to);
                var warning = connection.LastSyncedAt is DateTimeOffset last && last >= now.AddMinutes(-30) &&
                    connection.LastSyncError is null
                    ? "Calculated from the latest open Xero documents and unapplied credits."
                    : "Calculated from the locally available Xero documents. The Xero refresh is overdue or incomplete, so amounts may differ from the live report.";
                return new(connection.XeroTenantName, source, null, null, [$"As at {to:dd MMM yyyy}"], rows,
                    null, warning);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning("Xero ageing could not be calculated: {ErrorType}", ex.GetType().Name);
                return Empty(connection.XeroTenantName, source, null, "Open the current ageing report in Xero for the verified balance.");
            }
        }
        XeroReportResult Unavailable(string message) => saved is null
            ? Empty(connection.XeroTenantName, source, identity?.TaxBasis, message)
            : saved with { Message = $"{message} Showing the last successful Xero result for these dates." };

        if (from > to)
            return Unavailable("The start date must be on or before the end date.");
        if (kind == "balance-sheet" && to.Day != DateTime.DaysInMonth(to.Year, to.Month))
            return Unavailable("The Xero API supplies month-end Balance Sheets. For another cutoff, use the report in Xero.");
        if (endpoint is null && identity is not null)
            return Unavailable(DirectReportMessage(kind));
        if (connection.NextSyncAt is { } retry && retry > now &&
            connection.LastSyncError?.Contains("limit", StringComparison.OrdinalIgnoreCase) == true)
            return Unavailable("The Xero connection is waiting for its request limit to reset.");
        if (cache.TryGetValue<DateTimeOffset>((connection.Id, tenantId, "xero-report-backoff"), out var reportRetry) && reportRetry > now)
            return Unavailable("The Xero connection is waiting for its request limit to reset.");
        if (previousLease > now)
            return Unavailable("The Xero connection is busy refreshing. Reload the report after that refresh.");

        var claimed = await db.XeroConnections.Where(x => x.Id == connection.Id && x.IsActive &&
                x.XeroTenantId == tenantId && x.SyncLeaseUntil == previousLease &&
                x.SyncLeaseOwner == connection.SyncLeaseOwner)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.SyncLeaseUntil, now.AddMinutes(7))
                .SetProperty(x => x.SyncLeaseOwner, leaseOwner), ct);
        if (claimed == 0)
            return Unavailable("The Xero connection is busy refreshing. Reload the report after that refresh.");

        try
        {
            var connected = await db.XeroConnections.SingleAsync(x =>
                x.Id == connection.Id && x.SyncLeaseOwner == leaseOwner, ct);
            var token = await connections.GetAccessTokenAsync(connected, userId, ct);
            if (identity is null)
            {
                try
                {
                    identity = ParseOrganisation(await api.GetJsonAsync(token, tenantId, "Organisation", ct));
                    cache.Set(identityKey, identity, TimeSpan.FromHours(2));
                    source = SourceLink(identity, kind, from, to);
                }
                catch (XeroApiException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                {
                    logger.LogInformation("Xero organisation settings were unavailable for report routing.");
                }
            }

            if (endpoint is null)
                return Unavailable(DirectReportMessage(kind));

            if (kind == "gst")
            {
                var index = await api.GetJsonAsync(token, tenantId, endpoint, ct);
                var reportId = FindFinalisedGstReport(index, from, to);
                if (reportId is null)
                    return Unavailable("Open Xero for the GST return for this period. Finalised returns appear here when Xero provides an exact date match.");
                endpoint = $"Reports/{reportId}";
            }

            var result = ParseReport(await api.GetJsonAsync(token, tenantId, endpoint, ct),
                connected.XeroTenantName, source, identity?.TaxBasis, DateTimeOffset.UtcNow);
            cache.Set(reportKey, result, RetainFor);
            return result;
        }
        catch (XeroApiException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var delay = ex.RetryAfter is { } value && value > TimeSpan.Zero ? value : TimeSpan.FromMinutes(1);
            cache.Set((connection.Id, tenantId, "xero-report-backoff"), DateTimeOffset.UtcNow.Add(delay), delay);
            return Unavailable("Xero has paused further reporting requests until its request limit resets.");
        }
        catch (XeroApiException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            return Unavailable("The current Xero connection does not have permission to read this report.");
        }
        catch (XeroReconnectRequiredException)
        {
            return Unavailable("The Xero connection needs to be reauthorised before it can read reports.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            logger.LogWarning("Xero report {Kind} could not be loaded: {ErrorType}", kind, ex.GetType().Name);
            return Unavailable("Xero did not return a usable report for this request.");
        }
        finally
        {
            await db.XeroConnections.Where(x => x.Id == connection.Id && x.SyncLeaseOwner == leaseOwner)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.SyncLeaseUntil, (DateTimeOffset?)null)
                    .SetProperty(x => x.SyncLeaseOwner, (string?)null), CancellationToken.None);
        }
    }

    internal static string? Endpoint(string kind, DateOnly from, DateOnly to)
    {
        var range = $"fromDate={from:yyyy-MM-dd}&toDate={to:yyyy-MM-dd}";
        return kind switch
        {
            "profit-loss" => $"Reports/ProfitAndLoss?{range}&paymentsOnly=false",
            "balance-sheet" => $"Reports/BalanceSheet?date={to:yyyy-MM-dd}&paymentsOnly=false",
            "trial-balance" => $"Reports/TrialBalance?date={to:yyyy-MM-dd}&paymentsOnly=false",
            "bank-summary" => $"Reports/BankSummary?{range}",
            "budget" => $"Reports/BudgetSummary?date={to:yyyy-MM-dd}&periods=12&timeframe=1",
            "gst" => "Reports",
            _ => null
        };
    }

    internal static string? FindFinalisedGstReport(byte[] payload, DateOnly from, DateOnly to)
    {
        using var document = JsonDocument.Parse(payload);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in RequiredArray(document.RootElement, "Reports").EnumerateArray())
        {
            if (PropertyText(item, "ReportName")?.Contains("GST", StringComparison.OrdinalIgnoreCase) != true ||
                PropertyText(item, "ReportID") is not { } id || id.Length == 0 ||
                !(id.All(char.IsAsciiDigit) || Guid.TryParseExact(id, "D", out _)) ||
                PropertyText(item, "ReportDate")?.Split(" to ", StringSplitOptions.TrimEntries) is not [var first, var last] ||
                !DateOnly.TryParseExact(first, "d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var start) ||
                !DateOnly.TryParseExact(last, "d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var end) ||
                start != from || end != to)
                continue;

            ids.Add(id);
        }
        return ids.Count == 1 ? ids.Single() : null;
    }

    internal static XeroReportResult ParseReport(
        byte[] payload, string? organisation, Uri source, string? taxBasis, DateTimeOffset time)
    {
        using var document = JsonDocument.Parse(payload);
        var reports = RequiredArray(document.RootElement, "Reports");
        if (reports.GetArrayLength() != 1)
            throw new JsonException("Expected one Xero report.");
        var report = reports[0];
        var titles = report.TryGetProperty("ReportTitles", out var subtitle) && subtitle.ValueKind == JsonValueKind.Array
            ? subtitle.EnumerateArray().Select(x => x.ToString()).ToArray()
            : [];
        var rows = new List<XeroReportRow>();
        AppendRows(RequiredArray(report, "Rows"), rows, 0);
        if (rows.Count == 0)
            throw new JsonException("Xero returned a report without rows.");
        return new(organisation, source, taxBasis, PropertyText(report, "ReportName"), titles, rows, time, null);
    }

    internal static IReadOnlyList<XeroReportRow> RenderAgeing(IEnumerable<XeroOpenBalance> balances, DateOnly asAt)
    {
        var documents = balances.ToArray();
        var showUndatedCredits = documents.Any(document => document.DueDate is null);
        var columnsForReport = new List<string>
        {
            "Contact", "Current", "< 1 Month", "1 Month", "2 Months", "3 Months", "Older"
        };
        if (showUndatedCredits)
            columnsForReport.Add("Unapplied credits");
        columnsForReport.Add("Total");
        var rows = new List<XeroReportRow>
        {
            new("Header", null, columnsForReport, 0)
        };
        var oneMonthAgo = asAt.AddMonths(-1);
        var twoMonthsAgo = asAt.AddMonths(-2);
        var threeMonthsAgo = asAt.AddMonths(-3);
        var fourMonthsAgo = asAt.AddMonths(-4);
        var totals = new decimal[showUndatedCredits ? 7 : 6];
        foreach (var contact in documents.GroupBy(x => x.Contact).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var columns = new decimal[totals.Length];
            foreach (var item in contact)
            {
                var bucket = item.DueDate switch
                {
                    null => 6,
                    DateOnly due when due > asAt => 0,
                    DateOnly due when due > oneMonthAgo => 1,
                    DateOnly due when due > twoMonthsAgo => 2,
                    DateOnly due when due > threeMonthsAgo => 3,
                    DateOnly due when due > fourMonthsAgo => 4,
                    _ => 5
                };
                columns[bucket] += item.Amount;
                totals[bucket] += item.Amount;
            }
            rows.Add(Amounts("Row", contact.Key, columns));
        }
        rows.Add(Amounts("SummaryRow", "Total", totals));
        return rows;
    }

    private static XeroReportRow Amounts(string kind, string label, decimal[] columns) =>
        new(kind, null, [label, .. columns.Select(x => x.ToString("N2")), columns.Sum().ToString("N2")], 0);

    private static void AppendRows(JsonElement items, List<XeroReportRow> rows, int depth)
    {
        foreach (var item in items.EnumerateArray())
        {
            var kind = PropertyText(item, "RowType") ?? "Row";
            var title = PropertyText(item, "Title");
            var cells = item.TryGetProperty("Cells", out var values) && values.ValueKind == JsonValueKind.Array
                ? values.EnumerateArray().Select(x => PropertyText(x, "Value") ?? string.Empty).ToArray()
                : [];
            if (cells.Length > 0 || !string.IsNullOrWhiteSpace(title))
                rows.Add(new(kind, title, cells, depth));
            if (item.TryGetProperty("Rows", out var nested) && nested.ValueKind == JsonValueKind.Array)
                AppendRows(nested, rows, depth + 1);
        }
    }

    internal static OrganisationInfo ParseOrganisation(byte[] payload)
    {
        using var document = JsonDocument.Parse(payload);
        var organisations = RequiredArray(document.RootElement, "Organisations");
        if (organisations.GetArrayLength() != 1)
            throw new JsonException("Expected one Xero organisation.");
        var organisation = organisations[0];
        return new(PropertyText(organisation, "ShortCode"), PropertyText(organisation, "SalesTaxBasis"));
    }

    private static JsonElement RequiredArray(JsonElement value, string key) =>
        value.TryGetProperty(key, out var array) && array.ValueKind == JsonValueKind.Array
            ? array
            : throw new JsonException($"Xero response is missing {key}.");

    private static string? PropertyText(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind is not JsonValueKind.Null
            ? value.ToString()
            : null;

    private static string DirectReportMessage(string kind) => kind switch
    {
        "receivables" or "payables" => "Xero provides ageing through this connection one contact at a time. Open the organisation-wide ageing report in Xero for the complete balance.",
        "general-ledger" or "report-transactions" => "The Xero copy in Account Island does not include the full posted ledger. Open the authoritative ledger in Xero.",
        "gst" => "Use the Xero GST return for this filing period. The Xero reporting API provides finalised GST returns only.",
        "cash-summary" => "Open Xero’s Cash Summary for its full cash report. Account Island can also show the Xero Bank Summary.",
        "bank-reconciliation" => "Use the Bank Reconciliation report in Xero to compare statement and ledger balances.",
        "budget" => "Open the budget variance report in Xero for its budget and actual figures.",
        "group" => "Consolidation requires verified ledger balances for each company. The connected company's figures are available in Xero.",
        _ => "The authoritative report is available in Xero."
    };

    private static Uri SourceLink(OrganisationInfo? identity, string kind, DateOnly from, DateOnly to)
    {
        var shortCode = identity?.ShortCode;
        if (shortCode is null || shortCode.Length is < 2 or > 20 || shortCode[0] != '!' ||
            shortCode.Skip(1).Any(x => !char.IsAsciiLetterOrDigit(x) && x is not '-' and not '_'))
            return new(ReportsPage);

        var reportId = kind switch
        {
            "profit-loss" or "group" => 1016,
            "balance-sheet" => 1017,
            "trial-balance" => 1005,
            "receivables" => 1001,
            "payables" => 1300,
            "cash-summary" => 1339,
            "bank-summary" => 1065,
            "bank-reconciliation" => 1066,
            "general-ledger" => 1071,
            "report-transactions" => 1009,
            "budget" => 1019,
            _ => 0
        };
        if (kind == "gst")
            return new($"https://go.xero.com/app/{shortCode}/tax-return/return/nz-ir-gst?periodStart={from:yyyy-MM-dd}&periodEnd={to:yyyy-MM-dd}");
        return reportId == 0 ? new($"{ReportsPage}/{shortCode}") :
            new($"{ReportsPage}/{shortCode}/v1/Run/{reportId}");
    }

    private static XeroReportResult Empty(string? organisation, Uri source, string? basis, string message) =>
        new(organisation, source, basis, null, [], [], null, message);

    internal sealed record OrganisationInfo(string? ShortCode, string? TaxBasis);
}
