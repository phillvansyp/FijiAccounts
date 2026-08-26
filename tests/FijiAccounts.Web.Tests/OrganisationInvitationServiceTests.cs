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
        Assert.Equal(test.Organisation.Id, accepted.OrganisationId);
        Assert.Equal(test.Organisation.Id, reopened.OrganisationId);
        Assert.Contains("already have", reopened.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await test.Db.OrganisationMemberships.CountAsync(
            x => x.OrganisationId == test.Organisation.Id && x.UserId == user.Id));
        Assert.Equal(1, await test.Db.AuditEvents.CountAsync(x =>
            x.EntityType == nameof(OrganisationInvitation) &&
            x.EntityId == test.Db.OrganisationInvitations.Single().Id.ToString() &&
            x.EventType == "OrganisationInvitationAccepted"));
    }

    [Fact]
    public async Task GetDetailsAsync_ReturnsOnlyActiveInvitationDetails()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new OrganisationInvitationService(test.Db, test.Access);
        var issued = await service.IssueAsync(
            test.UserId,
            test.Organisation.Id,
            "approver@example.com",
            OrganisationRole.Approver);

        var details = await service.GetDetailsAsync(issued.Token);
        var unknown = await service.GetDetailsAsync("not-the-issued-token");

        Assert.NotNull(details);
        Assert.Equal("approver@example.com", details.Email);
        Assert.Equal(test.Organisation.LegalName, details.OrganisationName);
        Assert.Equal(OrganisationRole.Approver, details.Role);
        Assert.Null(unknown);

        var invitation = await test.Db.OrganisationInvitations.SingleAsync();
        invitation.RevokedAt = DateTimeOffset.UtcNow;
        await test.Db.SaveChangesAsync();
        Assert.Null(await service.GetDetailsAsync(issued.Token));
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

    [Fact]
    public async Task ListPendingAsync_ReturnsActiveAndExpiredButNotCompletedInvitations()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new OrganisationInvitationService(test.Db, test.Access);
        var now = DateTimeOffset.UtcNow;
        test.Db.OrganisationInvitations.AddRange(
            Invitation(test.Organisation.Id, "expired@example.com", "expired-token", now.AddMinutes(-1)),
            Invitation(test.Organisation.Id, "pending@example.com", "pending-token", now.AddDays(1)),
            Invitation(test.Organisation.Id, "accepted@example.com", "accepted-token", now.AddDays(1), acceptedAt: now),
            Invitation(test.Organisation.Id, "revoked@example.com", "revoked-token", now.AddDays(1), revokedAt: now));
        await test.Db.SaveChangesAsync();

        var pending = await service.ListPendingAsync(test.UserId, test.Organisation.Id);

        Assert.Equal(2, pending.Count);
        Assert.Equal("expired@example.com", pending[0].Email);
        Assert.True(pending[0].IsExpired);
        Assert.Equal("pending@example.com", pending[1].Email);
        Assert.False(pending[1].IsExpired);
    }

    [Fact]
    public async Task ReissueAsync_RevokesOldLinkAndRecordsAuditEvidence()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        const string oldToken = "old-pending-token";
        var original = Invitation(
            test.Organisation.Id,
            "approver@example.com",
            oldToken,
            DateTimeOffset.UtcNow.AddDays(1),
            OrganisationRole.Approver);
        test.Db.OrganisationInvitations.Add(original);
        await test.Db.SaveChangesAsync();
        var service = new OrganisationInvitationService(test.Db, test.Access);

        var replacement = await service.ReissueAsync(
            test.UserId,
            test.Organisation.Id,
            original.Id);

        Assert.Equal(original.Email, replacement.Email);
        Assert.Equal(original.Role, replacement.Role);
        Assert.Null(await service.GetDetailsAsync(oldToken));
        Assert.NotNull(await service.GetDetailsAsync(replacement.Token));
        Assert.NotNull((await test.Db.OrganisationInvitations.FindAsync(original.Id))!.RevokedAt);
        Assert.Single(await service.ListPendingAsync(test.UserId, test.Organisation.Id));
        var audit = await test.Db.AuditEvents.SingleAsync(x =>
            x.EventType == "OrganisationInvitationReissued");
        Assert.Equal(test.UserId, audit.UserId);
        Assert.Contains(original.Id.ToString(), audit.JsonData);
    }

    [Fact]
    public async Task RevokeAsync_InvalidatesLinkAndRecordsAuditEvidence()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        const string token = "revocable-token";
        var invitation = Invitation(
            test.Organisation.Id,
            "bookkeeper@example.com",
            token,
            DateTimeOffset.UtcNow.AddDays(1));
        test.Db.OrganisationInvitations.Add(invitation);
        await test.Db.SaveChangesAsync();
        var service = new OrganisationInvitationService(test.Db, test.Access);

        await service.RevokeAsync(test.UserId, test.Organisation.Id, invitation.Id);

        Assert.Null(await service.GetDetailsAsync(token));
        Assert.Empty(await service.ListPendingAsync(test.UserId, test.Organisation.Id));
        var audit = await test.Db.AuditEvents.SingleAsync(x =>
            x.EventType == "OrganisationInvitationRevoked");
        Assert.Equal(test.UserId, audit.UserId);
        Assert.Equal(invitation.Id.ToString(), audit.EntityId);
    }

    [Fact]
    public async Task PendingInvitationManagement_RejectsReadOnlyMember()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var invitation = Invitation(
            test.Organisation.Id,
            "pending@example.com",
            "pending-management-token",
            DateTimeOffset.UtcNow.AddDays(1));
        test.Db.OrganisationInvitations.Add(invitation);
        await test.Db.OrganisationMemberships
            .Where(x =>
                x.OrganisationId == test.Organisation.Id &&
                x.UserId == test.UserId)
            .ExecuteUpdateAsync(update =>
                update.SetProperty(x => x.Role, OrganisationRole.ReadOnly));
        await test.Db.SaveChangesAsync();
        var service = new OrganisationInvitationService(test.Db, test.Access);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ListPendingAsync(test.UserId, test.Organisation.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ReissueAsync(test.UserId, test.Organisation.Id, invitation.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.RevokeAsync(test.UserId, test.Organisation.Id, invitation.Id));
        Assert.Null(invitation.RevokedAt);
        Assert.Empty(await test.Db.AuditEvents.Where(x =>
            x.EventType == "OrganisationInvitationReissued" ||
            x.EventType == "OrganisationInvitationRevoked").ToListAsync());
    }

    private static OrganisationInvitation Invitation(
        Guid organisationId,
        string email,
        string token,
        DateTimeOffset expiresAt,
        OrganisationRole role = OrganisationRole.Bookkeeper,
        DateTimeOffset? acceptedAt = null,
        DateTimeOffset? revokedAt = null) =>
        new()
        {
            OrganisationId = organisationId,
            Email = email,
            Role = role,
            TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
            ExpiresAt = expiresAt,
            AcceptedAt = acceptedAt,
            RevokedAt = revokedAt
        };
}
