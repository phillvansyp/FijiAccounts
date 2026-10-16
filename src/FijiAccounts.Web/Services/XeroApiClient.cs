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
    string? response = null) : HttpRequestException(message, null, statusCode)
{
    public string? Response { get; } = response;
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
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var bytes = await ReadCheckedAsync(response, cancellationToken);
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
    {
        var uri = Uri.TryCreate(endpoint, UriKind.Absolute, out var absolute)
            ? absolute
            : new Uri(AccountingBase + endpoint.TrimStart('/'));
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("xero-tenant-id", tenantId.ToString());
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (attempt < 3 && response.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable)
            {
                var requestedDelay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(attempt);
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(requestedDelay.TotalSeconds, 1, 10)), cancellationToken);
                continue;
            }
            return await ReadCheckedAsync(response, cancellationToken);
        }
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
        using var response = await client.SendAsync(request, cancellationToken);
        var bytes = await ReadCheckedAsync(response, cancellationToken);
        var payload = JsonSerializer.Deserialize<TokenPayload>(bytes, JsonOptions)
            ?? throw new XeroApiException("Xero returned an empty token response.", response.StatusCode);
        if (string.IsNullOrWhiteSpace(payload.AccessToken))
        {
            throw new XeroApiException("Xero did not return an access token.", response.StatusCode);
        }
        var refresh = string.IsNullOrWhiteSpace(payload.RefreshToken) ? existingRefreshToken : payload.RefreshToken;
        if (string.IsNullOrWhiteSpace(refresh))
        {
            throw new XeroApiException("Xero did not grant offline access. Reconnect and approve offline access.", response.StatusCode);
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
        throw new XeroApiException(
            $"Xero returned {(int)response.StatusCode} {response.ReasonPhrase}.",
            response.StatusCode,
            body);
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
