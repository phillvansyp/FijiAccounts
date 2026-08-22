using System.Security.Cryptography;
using System.Text;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class OrganisationInvitationServiceTests
{
    [Fact]
    public async Task AcceptAsync_ReopeningAcceptedInvitationRemainsSuccessful()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        const string token = "repeatable-invitation-token";
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "invitee@example.com",
            NormalizedUserName = "INVITEE@EXAMPLE.COM",
            Email = "invitee@example.com",
            NormalizedEmail = "INVITEE@EXAMPLE.COM",
            EmailConfirmed = true
        };
        test.Db.Users.Add(user);
        test.Db.OrganisationInvitations.Add(new OrganisationInvitation
        {
            OrganisationId = test.Organisation.Id,
            Email = user.Email,
            Role = OrganisationRole.Bookkeeper,
            TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        });
        await test.Db.SaveChangesAsync();
        var service = new OrganisationInvitationService(test.Db);

        var accepted = await service.AcceptAsync(user.Id, user.Email, token);
        var reopened = await service.AcceptAsync(user.Id, user.Email, token);

        Assert.True(accepted.Succeeded);
        Assert.True(reopened.Succeeded);
        Assert.Contains("already have", reopened.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await test.Db.OrganisationMemberships.CountAsync(
            x => x.OrganisationId == test.Organisation.Id && x.UserId == user.Id));
    }
}
