using FijiAccounts.Web.Components.Account;
using Microsoft.AspNetCore.Identity;

namespace FijiAccounts.Web.Data;

public static class AccountMaintenanceCommand
{
    private const string ResetPasswordCommand = "reset-password";

    public static async Task<bool> TryRunAsync(
        WebApplication app,
        IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0 ||
            !string.Equals(
                arguments[0],
                ResetPasswordCommand,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var email = app.Configuration["Maintenance:ResetPassword:Email"];
        var password = app.Configuration["Maintenance:ResetPassword:Password"];

        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Password maintenance requires an email and password through configuration.");
        }

        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email)
            ?? throw new InvalidOperationException("The requested account was not found.");

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, password);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Password reset failed: " +
                string.Join("; ", result.Errors.Select(x => x.Description)));
        }

        AccountLockoutPolicy.ClearFailureState(user);
        result = await users.UpdateAsync(user);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Account unlock failed: " +
                string.Join("; ", result.Errors.Select(x => x.Description)));
        }

        Console.WriteLine("Password reset and lockout clearance completed.");
        return true;
    }
}
