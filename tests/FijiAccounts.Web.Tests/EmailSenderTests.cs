using FijiAccounts.Web.Components.Account;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.Extensions.Configuration;

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
}
