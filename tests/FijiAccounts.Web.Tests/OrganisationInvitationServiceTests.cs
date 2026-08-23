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
        var service = new OrganisationInvitationService(test.Db, test.Access);

        var accepted = await service.AcceptAsync(user.Id, user.Email, token);
        var reopened = await service.AcceptAsync(user.Id, user.Email, token);

        Assert.True(accepted.Succeeded);
        Assert.True(reopened.Succeeded);
        Assert.Contains("already have", reopened.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await test.Db.OrganisationMemberships.CountAsync(
            x => x.OrganisationId == test.Organisation.Id && x.UserId == user.Id));
        Assert.Equal(1, await test.Db.AuditEvents.CountAsync(x =>
            x.EntityType == nameof(OrganisationInvitation) &&
            x.EntityId == test.Db.OrganisationInvitations.Single().Id.ToString() &&
            x.EventType == "OrganisationInvitationAccepted"));
    }

    [Fact]
    public async Task IssueAsync_ReplacesPendingInvitationAndRecordsAuditEvidence()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new OrganisationInvitationService(test.Db, test.Access);

        var first = await service.IssueAsync(
            test.UserId,
            test.Organisation.Id,
            " NEW.MEMBER@Example.com ",
            OrganisationRole.Bookkeeper);
        var replacement = await service.IssueAsync(
            test.UserId,
            test.Organisation.Id,
            "new.member@example.com",
            OrganisationRole.Accountant);

        var invitations = (await test.Db.OrganisationInvitations
            .AsNoTracking()
            .Where(x => x.OrganisationId == test.Organisation.Id)
            .ToListAsync())
            .OrderBy(x => x.ExpiresAt)
            .ToList();
        Assert.Equal(2, invitations.Count);
        Assert.NotNull(invitations[0].RevokedAt);
        Assert.Null(invitations[1].RevokedAt);
        Assert.Equal("new.member@example.com", invitations[1].Email);
        Assert.Equal(OrganisationRole.Accountant, invitations[1].Role);
        Assert.NotEqual(first.Token, replacement.Token);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(replacement.Token))),
            invitations[1].TokenHash);

        var auditEvents = await test.Db.AuditEvents
            .AsNoTracking()
            .Where(x => x.EventType == "OrganisationInvitationIssued")
            .OrderBy(x => x.Id)
            .ToListAsync();
        Assert.Equal(2, auditEvents.Count);
        Assert.All(auditEvents, audit => Assert.Equal(test.UserId, audit.UserId));
        Assert.Contains("\"ReplacedPendingInvitations\":1", auditEvents[1].JsonData);
    }

    [Fact]
    public async Task IssueAsync_ReadOnlyMemberCannotCreateInvitation()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new OrganisationInvitationService(test.Db, test.Access);
        await test.Db.OrganisationMemberships
            .Where(x =>
                x.OrganisationId == test.Organisation.Id &&
                x.UserId == test.UserId)
            .ExecuteUpdateAsync(update =>
                update.SetProperty(x => x.Role, OrganisationRole.ReadOnly));
        var initialAuditCount = await test.Db.AuditEvents.CountAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.IssueAsync(
                test.UserId,
                test.Organisation.Id,
                "member@example.com",
                OrganisationRole.Bookkeeper));

        Assert.Empty(await test.Db.OrganisationInvitations.ToListAsync());
        Assert.Equal(initialAuditCount, await test.Db.AuditEvents.CountAsync());
    }
}
