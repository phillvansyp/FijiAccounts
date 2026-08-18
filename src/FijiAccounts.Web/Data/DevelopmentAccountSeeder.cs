using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using FijiAccounts.Domain.Accounting;

namespace FijiAccounts.Web.Data;

public static class DevelopmentAccountSeeder
{
    public static async Task SeedAsync(WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        await using var scope =
            app.Services.CreateAsyncScope();

        var db =
            scope.ServiceProvider
                .GetRequiredService<ApplicationDbContext>();

        // Keep existing local Fiji organisations aligned with additions
        // to the starter chart without changing posted accounting history.
        var fijiOrganisationIds =
            await db.Organisations
                .Where(x => x.CountryCode == "FJ")
                .Select(x => x.Id)
                .ToListAsync();

        foreach (var organisationId in fijiOrganisationIds)
        {
            var existingCodes =
                await db.LedgerAccounts
                    .Where(x =>
                        x.OrganisationId == organisationId)
                    .Select(x => x.Code)
                    .ToListAsync();

            if (!existingCodes.Contains("3200"))
            {
                db.LedgerAccounts.Add(
                    new LedgerAccount
                    {
                        OrganisationId = organisationId,
                        Code = "3200",
                        Name = "Opening Balance Equity",
                        Type = AccountType.Equity,
                        IsSystemAccount = true,
                        IsActive = true
                    });
            }

            if (!existingCodes.Contains("6400"))
            {
                db.LedgerAccounts.Add(
                    new LedgerAccount
                    {
                        OrganisationId = organisationId,
                        Code = "6400",
                        Name = "Bank Fees and Charges",
                        Type = AccountType.Expense,
                        IsSystemAccount = true,
                        IsActive = true
                    });
            }
        }

        await db.SaveChangesAsync();

        // Development login seeding is optional and should not prevent
        // starter-chart upgrades from running.
        var email =
            app.Configuration["DevSeed:Email"];

        var password =
            app.Configuration["DevSeed:Password"];

        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var users =
            scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();

        if (await users.FindByEmailAsync(email) is null)
        {
            var user =
                new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true
                };

            user.PasswordHash =
                users.PasswordHasher.HashPassword(
                    user,
                    password);

            var result =
                await users.CreateAsync(user);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    "Development user creation failed: " +
                    string.Join(
                        "; ",
                        result.Errors.Select(
                            x => x.Description)));
            }
        }
    }
}