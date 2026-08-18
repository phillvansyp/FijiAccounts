using System.Net;
using System.Net.Mail;

namespace FijiAccounts.Web.Services;

public interface ILoginCodeEmailSender
{
    Task SendAsync(string email, string code, CancellationToken cancellationToken = default);
}

public sealed class LoginCodeEmailSender(IConfiguration configuration, IWebHostEnvironment environment, ILogger<LoginCodeEmailSender> logger) : ILoginCodeEmailSender
{
    public async Task SendAsync(string email, string code, CancellationToken cancellationToken = default)
    {
        var host = configuration["Email:Smtp:Host"];
        if (string.IsNullOrWhiteSpace(host))
        {
            if (!environment.IsDevelopment()) throw new InvalidOperationException("Security email delivery is not configured.");
            logger.LogWarning("Development-only email verification code for {Email}: {Code}", email, code);
            return;
        }

        var port = configuration.GetValue("Email:Smtp:Port", 587);
        var from = configuration["Email:FromAddress"] ?? throw new InvalidOperationException("Email:FromAddress is not configured.");
        using var message = new MailMessage(from, email)
        {
            Subject = "Your Account Island sign-in code",
            Body = $"Your Account Island verification code is {code}. It expires shortly. If you did not try to sign in, change your password immediately.",
            IsBodyHtml = false
        };
        using var client = new SmtpClient(host, port)
        {
            EnableSsl = configuration.GetValue("Email:Smtp:UseSsl", true)
        };
        var username = configuration["Email:Smtp:Username"];
        if (!string.IsNullOrWhiteSpace(username))
            client.Credentials = new NetworkCredential(username, configuration["Email:Smtp:Password"]);
        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(message, cancellationToken);
    }
}
