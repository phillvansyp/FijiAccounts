namespace FijiAccounts.Web.Services;

public interface ILoginCodeEmailSender
{
    Task SendAsync(string email, string code, CancellationToken cancellationToken = default);
}

public sealed class LoginCodeEmailSender(IEmailDeliveryService delivery) : ILoginCodeEmailSender
{
    public Task SendAsync(string email, string code, CancellationToken cancellationToken = default) =>
        delivery.SendAsync(
            new TransactionalEmail(
                email,
                "Your Account Island sign-in code",
                $"Your Account Island verification code is {code}. It expires shortly. If you did not try to sign in, change your password immediately."),
            cancellationToken);
}
