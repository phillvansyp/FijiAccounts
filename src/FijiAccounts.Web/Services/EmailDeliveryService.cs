using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using System.Text.Json;

namespace FijiAccounts.Web.Services;

public sealed record TransactionalEmail(
    string Recipient,
    string Subject,
    string TextBody,
    string? HtmlBody = null,
    IReadOnlyList<TransactionalEmailAttachment>? Attachments = null)
{
    public IReadOnlyList<TransactionalEmailAttachment> Files => Attachments ?? [];
}

public sealed record TransactionalEmailAttachment(
    string FileName,
    string ContentType,
    byte[] Content);

public interface IEmailDeliveryService
{
    bool IsConfigured { get; }

    Task SendAsync(
        TransactionalEmail email,
        CancellationToken cancellationToken = default);
}

public sealed class SmtpEmailDeliveryService(IConfiguration configuration) : IEmailDeliveryService
{
    private string? Host => configuration["Email:Smtp:Host"];

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) &&
        !string.IsNullOrWhiteSpace(configuration["Email:FromAddress"]);

    public async Task SendAsync(
        TransactionalEmail email,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Email delivery is not configured.");

        var from = configuration["Email:FromAddress"]!;
        using var message = new MailMessage(from, email.Recipient)
        {
            Subject = email.Subject,
            Body = email.HtmlBody ?? email.TextBody,
            IsBodyHtml = email.HtmlBody is not null
        };
        if (email.HtmlBody is not null)
        {
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                email.TextBody,
                Encoding.UTF8,
                MediaTypeNames.Text.Plain));
        }
        foreach (var attachment in email.Files)
        {
            message.Attachments.Add(new Attachment(
                new MemoryStream(attachment.Content, writable: false),
                attachment.FileName,
                attachment.ContentType));
        }

        using var client = new SmtpClient(
            Host!,
            configuration.GetValue("Email:Smtp:Port", 587))
        {
            EnableSsl = configuration.GetValue("Email:Smtp:UseSsl", true),
            Timeout = configuration.GetValue("Email:Smtp:TimeoutMilliseconds", 15_000)
        };

        var username = configuration["Email:Smtp:Username"];
        if (!string.IsNullOrWhiteSpace(username))
        {
            client.Credentials = new NetworkCredential(
                username,
                configuration["Email:Smtp:Password"]);
        }

        await client.SendMailAsync(message, cancellationToken);
    }
}

public sealed class MicrosoftGraphEmailDeliveryService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory) : IEmailDeliveryService
{
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private string? accessToken;
    private DateTimeOffset accessTokenExpiresAt;

    private string? TenantId => configuration["Email:MicrosoftGraph:TenantId"];
    private string? ClientId => configuration["Email:MicrosoftGraph:ClientId"];
    private string? ClientSecret => configuration["Email:MicrosoftGraph:ClientSecret"];
    private string? FromAddress => configuration["Email:FromAddress"];

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TenantId) &&
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret) &&
        !string.IsNullOrWhiteSpace(FromAddress);

    public async Task SendAsync(
        TransactionalEmail email,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Email delivery is not configured.");

        var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(FromAddress!)}/sendMail");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await GetAccessTokenAsync(client, cancellationToken));
        var graphMessage = new Dictionary<string, object?>
        {
            ["subject"] = email.Subject,
            ["body"] = new
            {
                contentType = email.HtmlBody is null ? "Text" : "HTML",
                content = email.HtmlBody ?? email.TextBody
            },
            ["toRecipients"] = new[]
            {
                new
                {
                    emailAddress = new { address = email.Recipient }
                }
            }
        };
        if (email.Files.Count > 0)
        {
            graphMessage["attachments"] = email.Files.Select(attachment =>
                new Dictionary<string, object?>
                {
                    ["@odata.type"] = "#microsoft.graph.fileAttachment",
                    ["name"] = attachment.FileName,
                    ["contentType"] = attachment.ContentType,
                    ["contentBytes"] = Convert.ToBase64String(attachment.Content)
                }).ToArray();
        }

        request.Content = JsonContent.Create(new
        {
            message = graphMessage,
            saveToSentItems = true
        });

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Microsoft Graph email delivery failed with status {(int)response.StatusCode}: {responseBody}");
        }
    }

    private async Task<string> GetAccessTokenAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        if (accessToken is not null &&
            accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return accessToken;
        }

        await tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (accessToken is not null &&
                accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return accessToken;
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://login.microsoftonline.com/{Uri.EscapeDataString(TenantId!)}/oauth2/v2.0/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = ClientId!,
                    ["client_secret"] = ClientSecret!,
                    ["scope"] = "https://graph.microsoft.com/.default",
                    ["grant_type"] = "client_credentials"
                })
            };

            using var response = await client.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Microsoft Graph authentication failed with status {(int)response.StatusCode}: {responseBody}");
            }

            using var payload = JsonDocument.Parse(responseBody);
            accessToken = payload.RootElement.GetProperty("access_token").GetString()
                ?? throw new InvalidOperationException("Microsoft Graph returned an empty access token.");
            var expiresIn = payload.RootElement.TryGetProperty("expires_in", out var expiresInElement)
                ? expiresInElement.GetInt32()
                : 3600;
            accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return accessToken;
        }
        finally
        {
            tokenLock.Release();
        }
    }
}
