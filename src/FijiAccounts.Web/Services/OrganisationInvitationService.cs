using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record InvitationAcceptanceResult(bool Succeeded, string Message);
public sealed record InvitationIssueResult(string Token, DateTimeOffset ExpiresAt);

public sealed class OrganisationInvitationService(
    ApplicationDbContext db,
    TenantAccessService access)
{
    public async Task<InvitationIssueResult> IssueAsync(
        string userId,
        Guid organisationId,
        string email,
        OrganisationRole role,
        CancellationToken cancellationToken = default)
    {
        if (!await access.CanManageTeamAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot manage this organisation's team.");
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        if (normalizedEmail.Length is < 1 or > 320 ||
            !new EmailAddressAttribute().IsValid(normalizedEmail))
        {
            throw new InvalidOperationException("Enter a valid email address.");
        }

        if (!Enum.IsDefined(role) || role == OrganisationRole.Owner)
        {
            throw new InvalidOperationException("Select a valid invitation role.");
        }

        var now = DateTimeOffset.UtcNow;
        var pendingInvitations = await db.OrganisationInvitations
            .Where(x =>
                x.OrganisationId == organisationId &&
                x.Email == normalizedEmail &&
                x.AcceptedAt == null &&
                x.RevokedAt == null)
            .ToListAsync(cancellationToken);
        var replacedInvitations = pendingInvitations
            .Where(x => x.ExpiresAt > now)
            .ToList();
        foreach (var previous in replacedInvitations)
        {
            previous.RevokedAt = now;
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var invitation = new OrganisationInvitation
        {
            OrganisationId = organisationId,
            Email = normalizedEmail,
            Role = role,
            TokenHash = HashToken(token),
            ExpiresAt = now.AddDays(7)
        };
        db.OrganisationInvitations.Add(invitation);
        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = "OrganisationInvitationIssued",
            EntityType = nameof(OrganisationInvitation),
            EntityId = invitation.Id.ToString(),
            JsonData = JsonSerializer.Serialize(new
            {
                invitation.Email,
                Role = invitation.Role.ToString(),
                invitation.ExpiresAt,
                ReplacedPendingInvitations = replacedInvitations.Count
            })
        });
        await db.SaveChangesAsync(cancellationToken);

        return new(token, invitation.ExpiresAt);
    }

    public async Task<InvitationAcceptanceResult> AcceptAsync(
        string userId,
        string? email,
        string token,
        CancellationToken cancellationToken = default)
    {
        var hash = HashToken(token);
        var invitation = await db.OrganisationInvitations
            .Include(x => x.Organisation)
            .SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        if (invitation is null || invitation.ExpiresAt <= DateTimeOffset.UtcNow || invitation.RevokedAt is not null)
        {
            return new(false, "This invitation is invalid or has expired.");
        }

        if (!string.Equals(invitation.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            return new(false, $"This invitation was issued to {invitation.Email}. Sign in using that address.");
        }

        var isMember = await db.OrganisationMemberships.AnyAsync(
            x => x.OrganisationId == invitation.OrganisationId && x.UserId == userId,
            cancellationToken);
        if (invitation.AcceptedAt is not null)
        {
            return isMember
                ? new(true, $"You already have {invitation.Role} access to {invitation.Organisation.LegalName}.")
                : new(false, "This invitation has already been accepted.");
        }

        if (!isMember)
        {
            db.OrganisationMemberships.Add(new OrganisationMembership
            {
                OrganisationId = invitation.OrganisationId,
                UserId = userId,
                Role = invitation.Role
            });
        }

        invitation.AcceptedAt = DateTimeOffset.UtcNow;
        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = invitation.OrganisationId,
            UserId = userId,
            EventType = "OrganisationInvitationAccepted",
            EntityType = nameof(OrganisationInvitation),
            EntityId = invitation.Id.ToString(),
            JsonData = JsonSerializer.Serialize(new
            {
                invitation.Email,
                Role = invitation.Role.ToString()
            })
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(true, $"You now have {invitation.Role} access to {invitation.Organisation.LegalName}.");
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
