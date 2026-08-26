using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.AspNetCore.Identity;
using System.Net;

namespace FijiAccounts.Web.Components.Account;

internal sealed class IdentityEmailSender(IEmailDeliveryService delivery) : IEmailSender<ApplicationUser>
{
    public Task SendConfirmationLinkAsync(
        ApplicationUser user,
        string email,
        string confirmationLink) =>
        delivery.SendAsync(new TransactionalEmail(
            email,
            "Confirm your Account Island email",
            $"Confirm your Account Island email by opening this link: {WebUtility.HtmlDecode(confirmationLink)}",
            $"<p>Confirm your Account Island email by <a href=\"{confirmationLink}\">opening this secure link</a>.</p>"));

    public Task SendPasswordResetLinkAsync(
        ApplicationUser user,
        string email,
        string resetLink) =>
        delivery.SendAsync(new TransactionalEmail(
            email,
            "Reset your Account Island password",
            $"Reset your Account Island password by opening this link: {WebUtility.HtmlDecode(resetLink)}",
            $"<p>Reset your Account Island password by <a href=\"{resetLink}\">opening this secure link</a>.</p>"));

    public Task SendPasswordResetCodeAsync(
        ApplicationUser user,
        string email,
        string resetCode) =>
        delivery.SendAsync(new TransactionalEmail(
            email,
            "Reset your Account Island password",
            $"Your Account Island password reset code is: {resetCode}"));
}
