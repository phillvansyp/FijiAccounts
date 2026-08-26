using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record InvitationAcceptanceResult(
    bool Succeeded,
    string Message,
    Guid? OrganisationId = null);
public sealed record InvitationIssueResult(string Token, DateTimeOffset ExpiresAt);
public sealed record InvitationReissueResult(
    string Token,
    string Email,
    OrganisationRole Role,
    DateTimeOffset ExpiresAt);
public sealed record PendingInvitationSummary(
    Guid Id,
    string Email,
    OrganisationRole Role,
    DateTimeOffset ExpiresAt,
    bool IsExpired);
public sealed record InvitationDetails(
    string Email,
    string OrganisationName,
    OrganisationRole Role,
    DateTimeOffset ExpiresAt);

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
        var replacedInvitations = pendingInvitations;
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

    public async Task<IReadOnlyList<PendingInvitationSummary>> ListPendingAsync(
        string userId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        if (!await access.CanManageTeamAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot manage this organisation's team.");
        }

        var now = DateTimeOffset.UtcNow;
        var invitations = await db.OrganisationInvitations
            .AsNoTracking()
            .Where(x =>
                x.OrganisationId == organisationId &&
                x.AcceptedAt == null &&
                x.RevokedAt == null)
            .ToListAsync(cancellationToken);

        return invitations
            .OrderBy(x => x.ExpiresAt)
            .ThenBy(x => x.Email)
            .Select(x => new PendingInvitationSummary(
                x.Id,
                x.Email,
                x.Role,
                x.ExpiresAt,
                x.ExpiresAt <= now))
            .ToList();
    }

    public async Task<InvitationReissueResult> ReissueAsync(
        string userId,
        Guid organisationId,
        Guid invitationId,
        CancellationToken cancellationToken = default)
    {
        if (!await access.CanManageTeamAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot manage this organisation's team.");
        }

        var invitation = await db.OrganisationInvitations.SingleOrDefaultAsync(
            x =>
                x.Id == invitationId &&
                x.OrganisationId == organisationId &&
                x.AcceptedAt == null &&
                x.RevokedAt == null,
            cancellationToken);
        if (invitation is null)
        {
            throw new InvalidOperationException("This invitation is no longer pending.");
        }

        var now = DateTimeOffset.UtcNow;
        invitation.RevokedAt = now;

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var replacement = new OrganisationInvitation
        {
            OrganisationId = organisationId,
            Email = invitation.Email,
            Role = invitation.Role,
            TokenHash = HashToken(token),
            ExpiresAt = now.AddDays(7)
        };
        db.OrganisationInvitations.Add(replacement);
        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = "OrganisationInvitationReissued",
            EntityType = nameof(OrganisationInvitation),
            EntityId = replacement.Id.ToString(),
            JsonData = JsonSerializer.Serialize(new
            {
                replacement.Email,
                Role = replacement.Role.ToString(),
                replacement.ExpiresAt,
                ReplacedInvitationId = invitation.Id
            })
        });
        await db.SaveChangesAsync(cancellationToken);

        return new(token, replacement.Email, replacement.Role, replacement.ExpiresAt);
    }

    public async Task RevokeAsync(
        string userId,
        Guid organisationId,
        Guid invitationId,
        CancellationToken cancellationToken = default)
    {
        if (!await access.CanManageTeamAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot manage this organisation's team.");
        }

        var invitation = await db.OrganisationInvitations.SingleOrDefaultAsync(
            x =>
                x.Id == invitationId &&
                x.OrganisationId == organisationId &&
                x.AcceptedAt == null &&
                x.RevokedAt == null,
            cancellationToken);
        if (invitation is null)
        {
            throw new InvalidOperationException("This invitation is no longer pending.");
        }

        invitation.RevokedAt = DateTimeOffset.UtcNow;
        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = "OrganisationInvitationRevoked",
            EntityType = nameof(OrganisationInvitation),
            EntityId = invitation.Id.ToString(),
            JsonData = JsonSerializer.Serialize(new
            {
                invitation.Email,
                Role = invitation.Role.ToString()
            })
        });
        await db.SaveChangesAsync(cancellationToken);
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
                ? new(
                    true,
                    $"You already have {invitation.Role} access to {invitation.Organisation.LegalName}.",
                    invitation.OrganisationId)
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
        return new(
            true,
            $"You now have {invitation.Role} access to {invitation.Organisation.LegalName}.",
            invitation.OrganisationId);
    }

    public async Task<InvitationDetails?> GetDetailsAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var hash = HashToken(token);
        var invitation = await db.OrganisationInvitations
            .AsNoTracking()
            .Include(x => x.Organisation)
            .Where(x => x.TokenHash == hash)
            .SingleOrDefaultAsync(cancellationToken);
        if (invitation is null ||
            invitation.ExpiresAt <= DateTimeOffset.UtcNow ||
            invitation.RevokedAt is not null)
        {
            return null;
        }

        return new InvitationDetails(
            invitation.Email,
            invitation.Organisation.LegalName,
            invitation.Role,
            invitation.ExpiresAt);
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
