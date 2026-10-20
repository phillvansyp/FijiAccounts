using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace FijiAccounts.Web.Services;

public sealed class XeroApiOptions
{
    public const string SectionName = "Xero";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string WebhookKey { get; set; } = string.Empty;
    public int SyncIntervalMinutes { get; set; } = 15;
    public int FullReconciliationHours { get; set; } = 24;
    public int RequestTimeoutSeconds { get; set; } = 120;
    public string Scopes { get; set; } =
        "openid profile email offline_access accounting.settings.read accounting.contacts.read " +
        "accounting.invoices.read accounting.payments.read accounting.banktransactions.read " +
        "accounting.manualjournals.read accounting.reports.trialbalance.read " +
        "accounting.reports.profitandloss.read accounting.reports.balancesheet.read " +
        "accounting.reports.aged.read accounting.attachments.read accounting.budgets.read assets.read";
}

public sealed record XeroToken(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn,
    string Scope);

public sealed record XeroTenantCandidate(
    string ConnectionId,
    Guid TenantId,
    string TenantName,
    string TenantType);

public sealed class XeroApiException(
    string message,
    System.Net.HttpStatusCode statusCode,
    string? response = null,
    TimeSpan? retryAfter = null,
    string? rateLimitProblem = null) : HttpRequestException(message, null, statusCode)
{
    public string? Response { get; } = response;
    public TimeSpan? RetryAfter { get; } = retryAfter;
    public string? RateLimitProblem { get; } = rateLimitProblem;
}

public sealed class XeroApiClient(HttpClient client, IOptions<XeroApiOptions> configured)
{
    private const int MaximumResponseBytes = 30 * 1024 * 1024;
    private const string TokenEndpoint = "https://identity.xero.com/connect/token";
    private const string ConnectionsEndpoint = "https://api.xero.com/connections";
    private const string AccountingBase = "https://api.xero.com/api.xro/2.0/";
    private readonly XeroApiOptions options = configured.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<XeroToken> ExchangeCodeAsync(string code, CancellationToken cancellationToken = default) =>
        await RequestTokenAsync([
            new("grant_type", "authorization_code"),
            new("code", code),
            new("redirect_uri", options.RedirectUri)
        ], null, cancellationToken);

    public async Task<XeroToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default) =>
        await RequestTokenAsync([
            new("grant_type", "refresh_token"),
            new("refresh_token", refreshToken)
        ], refreshToken, cancellationToken);

    public async Task<IReadOnlyList<XeroTenantCandidate>> GetConnectionsAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ConnectionsEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var bytes = await SendAndReadCheckedAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var payload = JsonSerializer.Deserialize<List<ConnectionPayload>>(bytes, JsonOptions) ?? [];
        return payload
            .Where(x => x.TenantId != Guid.Empty && !string.IsNullOrWhiteSpace(x.Id))
            .Select(x => new XeroTenantCandidate(
                x.Id!, x.TenantId, x.TenantName?.Trim() ?? "Xero organisation", x.TenantType?.Trim() ?? "ORGANISATION"))
            .ToList();
    }

    public async Task<byte[]> GetJsonAsync(
        string accessToken,
        Guid tenantId,
        string endpoint,
        CancellationToken cancellationToken = default)
        => await GetAsync(accessToken, tenantId, endpoint, "application/json", null, cancellationToken);

    public async Task<byte[]> GetJsonAsync(
        string accessToken,
        Guid tenantId,
        string endpoint,
        DateTimeOffset? ifModifiedSince,
        CancellationToken cancellationToken = default)
        => await GetAsync(accessToken, tenantId, endpoint, "application/json", ifModifiedSince, cancellationToken);

    public async Task<byte[]> GetBytesAsync(
        string accessToken,
        Guid tenantId,
        string endpoint,
        string? contentType = null,
        CancellationToken cancellationToken = default)
        => await GetAsync(
            accessToken,
            tenantId,
            endpoint,
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            null,
            cancellationToken);

    private async Task<byte[]> GetAsync(
        string accessToken,
        Guid tenantId,
        string endpoint,
        string accept,
        DateTimeOffset? ifModifiedSince,
        CancellationToken cancellationToken)
    {
        var uri = Uri.TryCreate(endpoint, UriKind.Absolute, out var absolute)
            ? absolute
            : new Uri(AccountingBase + endpoint.TrimStart('/'));
        if (!uri.AbsoluteUri.StartsWith(AccountingBase, StringComparison.OrdinalIgnoreCase) &&
            !uri.Host.Equals("api.xero.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Xero content must be downloaded from the Xero API.");
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        request.Headers.IfModifiedSince = ifModifiedSince;
        if (!accept.Equals("application/json", StringComparison.OrdinalIgnoreCase))
            request.Headers.TryAddWithoutValidation("contentType", accept);
        request.Headers.Add("xero-tenant-id", tenantId.ToString());
        return await SendAndReadCheckedAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private async Task<XeroToken> RequestTokenAsync(
        IReadOnlyList<KeyValuePair<string, string>> fields,
        string? existingRefreshToken,
        CancellationToken cancellationToken)
    {
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ClientId}:{options.ClientSecret}"));
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(fields)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        var bytes = await SendAndReadCheckedAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        var statusCode = System.Net.HttpStatusCode.OK;
        var payload = JsonSerializer.Deserialize<TokenPayload>(bytes, JsonOptions)
            ?? throw new XeroApiException("Xero returned an empty token response.", statusCode);
        if (string.IsNullOrWhiteSpace(payload.AccessToken))
        {
            throw new XeroApiException("Xero did not return an access token.", statusCode);
        }
        var refresh = string.IsNullOrWhiteSpace(payload.RefreshToken) ? existingRefreshToken : payload.RefreshToken;
        if (string.IsNullOrWhiteSpace(refresh))
        {
            throw new XeroApiException("Xero did not grant offline access. Reconnect and approve offline access.", statusCode);
        }
        return new XeroToken(payload.AccessToken, refresh, Math.Max(60, payload.ExpiresIn), payload.Scope ?? string.Empty);
    }

    private static async Task<byte[]> ReadCheckedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            throw new XeroApiException("Xero returned a response larger than the safe import limit.", response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length > MaximumResponseBytes)
            throw new XeroApiException("Xero returned a response larger than the safe import limit.", response.StatusCode);
        if (response.IsSuccessStatusCode) return bytes;
        var body = Encoding.UTF8.GetString(bytes);
        if (body.Length > 600) body = body[..600];
        var retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null && response.Headers.RetryAfter?.Date is { } retryAt)
            retryAfter = retryAt - DateTimeOffset.UtcNow;
        if (retryAfter < TimeSpan.Zero) retryAfter = TimeSpan.Zero;
        var rateLimitProblem = response.Headers.TryGetValues("X-Rate-Limit-Problem", out var problems)
            ? problems.FirstOrDefault()
            : null;
        throw new XeroApiException(
            $"Xero returned {(int)response.StatusCode} {response.ReasonPhrase}.",
            response.StatusCode,
            body,
            retryAfter,
            rateLimitProblem);
    }

    private async Task<byte[]> SendAndReadCheckedAsync(
        HttpRequestMessage request,
        HttpCompletionOption completion,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.RequestTimeoutSeconds, 1, 600)));
        try
        {
            using var response = await client.SendAsync(request, completion, timeout.Token);
            return await ReadCheckedAsync(response, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new XeroApiException(
                "Xero did not respond before the request timed out. Try the copy again; completed datasets have been retained.",
                System.Net.HttpStatusCode.RequestTimeout);
        }
    }

    private sealed class TokenPayload
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("scope")] public string? Scope { get; set; }
    }

    private sealed class ConnectionPayload
    {
        public string? Id { get; set; }
        public Guid TenantId { get; set; }
        public string? TenantName { get; set; }
        public string? TenantType { get; set; }
    }
}
