using FijiAccounts.Web.Components.Account;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FijiAccounts.Web.Tests;

public sealed class EmailSenderTests
{
    [Fact]
    public async Task IdentitySender_BuildsConfirmationEmailWithUsableTextLink()
    {
        var delivery = new RecordingEmailDeliveryService();
        var sender = new IdentityEmailSender(delivery);

        await sender.SendConfirmationLinkAsync(
            new ApplicationUser(),
            "person@example.com",
            "https://example.com/confirm?user=1&amp;code=abc");

        var email = Assert.Single(delivery.Messages);
        Assert.Equal("person@example.com", email.Recipient);
        Assert.Contains("Confirm", email.Subject, StringComparison.Ordinal);
        Assert.Contains("?user=1&code=abc", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("?user=1&amp;code=abc", email.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IdentitySender_BuildsPasswordResetEmail()
    {
        var delivery = new RecordingEmailDeliveryService();
        var sender = new IdentityEmailSender(delivery);

        await sender.SendPasswordResetLinkAsync(
            new ApplicationUser(),
            "person@example.com",
            "https://example.com/reset?code=abc");

        var email = Assert.Single(delivery.Messages);
        Assert.Contains("Reset", email.Subject, StringComparison.Ordinal);
        Assert.Contains("https://example.com/reset?code=abc", email.TextBody, StringComparison.Ordinal);
        Assert.NotNull(email.HtmlBody);
    }

    [Fact]
    public async Task LoginCodeSender_DeliversCodeAsPlainText()
    {
        var delivery = new RecordingEmailDeliveryService();
        var sender = new LoginCodeEmailSender(delivery);

        await sender.SendAsync("person@example.com", "123456");

        var email = Assert.Single(delivery.Messages);
        Assert.Contains("123456", email.TextBody, StringComparison.Ordinal);
        Assert.Null(email.HtmlBody);
    }

    [Fact]
    public async Task InvitationSender_EncodesOrganisationAndLinkInHtml()
    {
        var delivery = new RecordingEmailDeliveryService();
        var sender = new OrganisationInvitationEmailSender(delivery);

        await sender.SendAsync(
            "person@example.com",
            "A&B <Limited>",
            OrganisationRole.Administrator,
            "https://example.com/invite?token=one&source=two");

        var email = Assert.Single(delivery.Messages);
        Assert.Contains("A&amp;B &lt;Limited&gt;", email.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("token=one&amp;source=two", email.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("A&B <Limited>", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("Create your password", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("Create your password", email.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SmtpDelivery_RejectsMissingConfiguration()
    {
        var configuration = new ConfigurationBuilder().Build();
        var delivery = new SmtpEmailDeliveryService(configuration);

        Assert.False(delivery.IsConfigured);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            delivery.SendAsync(new TransactionalEmail(
                "person@example.com",
                "Subject",
                "Body")));
        Assert.Equal("Email delivery is not configured.", exception.Message);
    }

    [Fact]
    public async Task MicrosoftGraphDelivery_AuthenticatesAndSendsMessage()
    {
        var handler = new RecordingHttpMessageHandler();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:FromAddress"] = "accountisland@1cr.co.nz",
                ["Email:MicrosoftGraph:TenantId"] = "tenant-id",
                ["Email:MicrosoftGraph:ClientId"] = "client-id",
                ["Email:MicrosoftGraph:ClientSecret"] = "client-secret"
            })
            .Build();
        var delivery = new MicrosoftGraphEmailDeliveryService(
            configuration,
            new StubHttpClientFactory(new HttpClient(handler)));

        await delivery.SendAsync(new TransactionalEmail(
            "person@example.com",
            "Reset your password",
            "Open the reset link.",
            "<p>Open the reset link.</p>"));

        Assert.True(delivery.IsConfigured);
        Assert.Equal(2, handler.Requests.Count);
        var tokenRequest = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, tokenRequest.Method);
        Assert.Contains("tenant-id/oauth2/v2.0/token", tokenRequest.Uri, StringComparison.Ordinal);
        Assert.Contains("client_secret=client-secret", tokenRequest.Body, StringComparison.Ordinal);

        var sendRequest = handler.Requests[1];
        Assert.Equal(
            "https://graph.microsoft.com/v1.0/users/accountisland%401cr.co.nz/sendMail",
            sendRequest.Uri);
        Assert.Equal("Bearer access-token", sendRequest.Authorization);
        using var message = JsonDocument.Parse(sendRequest.Body);
        Assert.Equal(
            "person@example.com",
            message.RootElement
                .GetProperty("message")
                .GetProperty("toRecipients")[0]
                .GetProperty("emailAddress")
                .GetProperty("address")
                .GetString());
        Assert.Equal(
            "HTML",
            message.RootElement
                .GetProperty("message")
                .GetProperty("body")
                .GetProperty("contentType")
                .GetString());
    }

    [Fact]
    public async Task MicrosoftGraphDelivery_ReusesUnexpiredAccessToken()
    {
        var handler = new RecordingHttpMessageHandler();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:FromAddress"] = "accountisland@1cr.co.nz",
                ["Email:MicrosoftGraph:TenantId"] = "tenant-id",
                ["Email:MicrosoftGraph:ClientId"] = "client-id",
                ["Email:MicrosoftGraph:ClientSecret"] = "client-secret"
            })
            .Build();
        var delivery = new MicrosoftGraphEmailDeliveryService(
            configuration,
            new StubHttpClientFactory(new HttpClient(handler)));
        var email = new TransactionalEmail("person@example.com", "Subject", "Body");

        await delivery.SendAsync(email);
        await delivery.SendAsync(email);

        Assert.Equal(1, handler.Requests.Count(request =>
            request.Uri.Contains("/oauth2/v2.0/token", StringComparison.Ordinal)));
        Assert.Equal(2, handler.Requests.Count(request =>
            request.Uri.EndsWith("/sendMail", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task MicrosoftGraphDelivery_RejectsIncompleteConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:FromAddress"] = "accountisland@1cr.co.nz",
                ["Email:MicrosoftGraph:TenantId"] = "tenant-id"
            })
            .Build();
        var delivery = new MicrosoftGraphEmailDeliveryService(
            configuration,
            new StubHttpClientFactory(new HttpClient()));

        Assert.False(delivery.IsConfigured);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            delivery.SendAsync(new TransactionalEmail(
                "person@example.com",
                "Subject",
                "Body")));
        Assert.Equal("Email delivery is not configured.", exception.Message);
    }

    private sealed class RecordingEmailDeliveryService : IEmailDeliveryService
    {
        public bool IsConfigured => true;
        public List<TransactionalEmail> Messages { get; } = [];

        public Task SendAsync(
            TransactionalEmail email,
            CancellationToken cancellationToken = default)
        {
            Messages.Add(email);
            return Task.CompletedTask;
        }
    }

    private sealed record RecordedHttpRequest(
        HttpMethod Method,
        string Uri,
        string Body,
        string? Authorization);

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public List<RecordedHttpRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedHttpRequest(
                request.Method,
                request.RequestUri!.ToString(),
                request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.Authorization?.ToString()));

            if (request.RequestUri!.Host.Equals(
                "login.microsoftonline.com",
                StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        access_token = "access-token",
                        expires_in = 3600
                    })
                };
            }

            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
