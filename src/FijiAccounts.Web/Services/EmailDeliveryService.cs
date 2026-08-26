using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;

namespace FijiAccounts.Web.Services;

public sealed record TransactionalEmail(
    string Recipient,
    string Subject,
    string TextBody,
    string? HtmlBody = null);

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
