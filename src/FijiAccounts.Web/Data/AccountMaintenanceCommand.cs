using FijiAccounts.Web.Components.Account;
using FijiAccounts.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Data;

public static class AccountMaintenanceCommand
{
    private const string ResetPasswordCommand = "reset-password";
    private const string VerifyDatabaseCommand = "verify-database";
    private const string SendOperationsAlertCommand = "send-operations-alert";

    public static async Task<bool> TryRunAsync(
        WebApplication app,
        IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
        {
            return false;
        }

        if (string.Equals(
                arguments[0],
                VerifyDatabaseCommand,
                StringComparison.OrdinalIgnoreCase))
        {
            await using var verificationScope = app.Services.CreateAsyncScope();
            var database = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var requireCurrentMigrations = app.Configuration.GetValue(
                "Maintenance:VerifyDatabase:RequireCurrentMigrations",
                true);
            await VerifyDatabaseAsync(database, requireCurrentMigrations);
            Console.WriteLine("Database integrity and migration verification completed.");
            return true;
        }

        if (string.Equals(
                arguments[0],
                SendOperationsAlertCommand,
                StringComparison.OrdinalIgnoreCase))
        {
            await using var alertScope = app.Services.CreateAsyncScope();
            var delivery = alertScope.ServiceProvider
                .GetRequiredService<IEmailDeliveryService>();
            await SendOperationsAlertAsync(delivery, app.Configuration);
            Console.WriteLine("Operations alert sent.");
            return true;
        }

        if (!string.Equals(
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

    public static async Task VerifyDatabaseAsync(
        ApplicationDbContext database,
        bool requireCurrentMigrations = true,
        CancellationToken cancellationToken = default)
    {
        if (!await database.Database.CanConnectAsync(cancellationToken))
        {
            throw new InvalidOperationException("The database is not reachable.");
        }

        await database.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = database.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA quick_check;";
            var result = await command.ExecuteScalarAsync(cancellationToken);
            if (!string.Equals(result?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"SQLite integrity verification failed: {result ?? "no result"}.");
            }
        }
        finally
        {
            await database.Database.CloseConnectionAsync();
        }

        if (requireCurrentMigrations)
        {
            var pendingMigrations = await database.Database
                .GetPendingMigrationsAsync(cancellationToken);
            var pending = pendingMigrations.ToArray();
            if (pending.Length > 0)
            {
                throw new InvalidOperationException(
                    $"The database has {pending.Length} pending migration(s): {string.Join(", ", pending)}.");
            }
        }
    }

    public static async Task SendOperationsAlertAsync(
        IEmailDeliveryService delivery,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var recipient = configuration["Maintenance:OperationsAlert:Recipient"];
        var subject = configuration["Maintenance:OperationsAlert:Subject"];
        var body = configuration["Maintenance:OperationsAlert:Body"];
        if (string.IsNullOrWhiteSpace(recipient) ||
            string.IsNullOrWhiteSpace(subject) ||
            string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidOperationException(
                "Operations alert delivery requires a recipient, subject and body through configuration.");
        }

        if (!delivery.IsConfigured)
        {
            throw new InvalidOperationException("Email delivery is not configured.");
        }

        await delivery.SendAsync(
            new TransactionalEmail(
                recipient.Trim(),
                subject.Trim(),
                body.Trim()),
            cancellationToken);
    }
}
