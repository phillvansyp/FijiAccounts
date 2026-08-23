using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed class PlatformAdminAccessService(ApplicationDbContext db)
{
    public const string RoleName = "PlatformAdministrator";
    public const string PolicyName = "PlatformAdministrator";

    public Task<bool> IsPlatformAdministratorAsync(
        string userId,
        CancellationToken ct = default) =>
        db.UserRoles.AnyAsync(
            userRole =>
                userRole.UserId == userId &&
                db.Roles.Any(role =>
                    role.Id == userRole.RoleId &&
                    role.NormalizedName == RoleName.ToUpperInvariant()),
            ct);
}

public static class PlatformAdminSeeder
{
    public static async Task SeedAsync(WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (!await roles.RoleExistsAsync(PlatformAdminAccessService.RoleName))
        {
            var result = await roles.CreateAsync(new IdentityRole(PlatformAdminAccessService.RoleName));
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    "Platform administrator role creation failed: " +
                    string.Join("; ", result.Errors.Select(x => x.Description)));
            }
        }

        var configuredEmail = ResolveAdministratorEmail(
            app.Configuration,
            app.Environment.IsDevelopment());
        ApplicationUser? administrator = null;

        if (!string.IsNullOrWhiteSpace(configuredEmail))
        {
            administrator = await users.FindByEmailAsync(configuredEmail.Trim());
        }
        else if (app.Environment.IsDevelopment())
        {
            administrator = await users.Users.OrderBy(x => x.Id).FirstOrDefaultAsync();
        }

        if (administrator is not null &&
            !await users.IsInRoleAsync(administrator, PlatformAdminAccessService.RoleName))
        {
            var result = await users.AddToRoleAsync(administrator, PlatformAdminAccessService.RoleName);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    "Platform administrator assignment failed: " +
                    string.Join("; ", result.Errors.Select(x => x.Description)));
            }
        }
    }

    internal static string? ResolveAdministratorEmail(
        IConfiguration configuration,
        bool isDevelopment)
    {
        var configuredEmail = configuration["PlatformAdmin:Email"];
        return string.IsNullOrWhiteSpace(configuredEmail) && isDevelopment
            ? configuration["DevSeed:Email"]
            : configuredEmail;
    }
}
