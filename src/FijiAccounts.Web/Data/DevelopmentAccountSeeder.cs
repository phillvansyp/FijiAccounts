using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using FijiAccounts.Domain.Accounting;

namespace FijiAccounts.Web.Data;

public static class DevelopmentAccountSeeder
{
    public static async Task SeedAsync(WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return;
        var email = app.Configuration["DevSeed:Email"];
        var password = app.Configuration["DevSeed:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return;

        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.FindByEmailAsync(email) is null)
        {
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            user.PasswordHash = users.PasswordHasher.HashPassword(user, password);
            var result = await users.CreateAsync(user);
            if (!result.Succeeded)
                throw new InvalidOperationException("Development user creation failed: " + string.Join("; ", result.Errors.Select(x => x.Description)));
        }

        // Keep existing local Fiji workspaces aligned with additions to the
        // starter chart without changing any posted accounting history.
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var fijiOrganisationIds = await db.Organisations
            .Where(x => x.CountryCode == "FJ")
            .Select(x => x.Id)
            .ToListAsync();
        var organisationsWithBankFees = await db.LedgerAccounts
            .Where(x => fijiOrganisationIds.Contains(x.OrganisationId) && x.Code == "6400")
            .Select(x => x.OrganisationId)
            .ToListAsync();
        foreach (var organisationId in fijiOrganisationIds.Except(organisationsWithBankFees))
        {
            db.LedgerAccounts.Add(new LedgerAccount
            {
                OrganisationId = organisationId,
                Code = "6400",
                Name = "Bank Fees and Charges",
                Type = AccountType.Expense,
                IsSystemAccount = true,
                IsActive = true
            });
        }
        await db.SaveChangesAsync();
    }
}
