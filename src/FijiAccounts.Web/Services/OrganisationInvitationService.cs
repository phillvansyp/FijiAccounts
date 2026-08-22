using System.Security.Cryptography;
using System.Text;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record InvitationAcceptanceResult(bool Succeeded, string Message);

public sealed class OrganisationInvitationService(ApplicationDbContext db)
{
    public async Task<InvitationAcceptanceResult> AcceptAsync(
        string userId,
        string? email,
        string token,
        CancellationToken cancellationToken = default)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
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
        await db.SaveChangesAsync(cancellationToken);
        return new(true, $"You now have {invitation.Role} access to {invitation.Organisation.LegalName}.");
    }
}
