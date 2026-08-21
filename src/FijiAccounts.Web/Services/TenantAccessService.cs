using Microsoft.EntityFrameworkCore;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record AccessibleOrganisation(Organisation Organisation, string AccessLabel, bool IsClient);

public sealed class TenantAccessService(ApplicationDbContext db)
{
    public async Task<List<AccessibleOrganisation>> ListAsync(string userId)
    {
        var direct = await db.OrganisationMemberships.AsNoTracking().Include(x => x.Organisation)
            .Where(x => x.UserId == userId).Select(x => new AccessibleOrganisation(x.Organisation, x.Role.ToString(), false)).ToListAsync();

        var practiceIds = await db.OrganisationMemberships.AsNoTracking()
            .Where(x => x.UserId == userId && x.Organisation.Kind == OrganisationKind.AccountingPractice &&
                (x.Role == OrganisationRole.Owner || x.Role == OrganisationRole.Administrator ||
                 x.Role == OrganisationRole.Accountant || x.Role == OrganisationRole.Bookkeeper))
            .Select(x => x.OrganisationId).ToArrayAsync();
        var clients = await db.AccountantEngagements.AsNoTracking().Include(x => x.ClientOrganisation)
            .Where(x => practiceIds.Contains(x.PracticeOrganisationId) && x.RevokedAt == null)
            .Select(x => new AccessibleOrganisation(x.ClientOrganisation, x.Access.ToString(), true)).ToListAsync();

        return direct.Concat(clients).GroupBy(x => x.Organisation.Id).Select(x => x.First())
            .OrderBy(x => x.Organisation.LegalName).ToList();
    }

    public async Task<AccessibleOrganisation?> FindAsync(string userId, Guid organisationId) =>
        (await ListAsync(userId)).SingleOrDefault(x => x.Organisation.Id == organisationId);

    public async Task<bool> CanManageTeamAsync(string userId, Guid organisationId) =>
        await db.OrganisationMemberships.AnyAsync(x => x.UserId == userId && x.OrganisationId == organisationId &&
            (x.Role == OrganisationRole.Owner || x.Role == OrganisationRole.Administrator));

    public async Task<bool> CanPostJournalsAsync(string userId, Guid organisationId)
    {
        if (await db.OrganisationMemberships.AnyAsync(x => x.UserId == userId && x.OrganisationId == organisationId &&
            (x.Role == OrganisationRole.Owner || x.Role == OrganisationRole.Administrator || x.Role == OrganisationRole.Accountant || x.Role == OrganisationRole.Bookkeeper))) return true;

        return await db.AccountantEngagements.AnyAsync(e => e.ClientOrganisationId == organisationId && e.RevokedAt == null &&
            e.Access != EngagementAccess.ReadOnly && db.OrganisationMemberships.Any(m => m.OrganisationId == e.PracticeOrganisationId && m.UserId == userId &&
                (m.Role == OrganisationRole.Owner || m.Role == OrganisationRole.Administrator || m.Role == OrganisationRole.Accountant || m.Role == OrganisationRole.Bookkeeper)));
    }

    public async Task<bool> CanManageContactsAsync(
    string userId,
    Guid organisationId)
{
    if (await db.OrganisationMemberships.AnyAsync(
        x =>
            x.UserId == userId &&
            x.OrganisationId == organisationId &&
            (
                x.Role == OrganisationRole.Owner ||
                x.Role == OrganisationRole.Administrator ||
                x.Role == OrganisationRole.Accountant ||
                x.Role == OrganisationRole.Bookkeeper
            )))
    {
        return true;
    }

    return await db.AccountantEngagements.AnyAsync(
        e =>
            e.ClientOrganisationId == organisationId &&
            e.RevokedAt == null &&
            e.Access != EngagementAccess.ReadOnly &&
            db.OrganisationMemberships.Any(
                m =>
                    m.OrganisationId == e.PracticeOrganisationId &&
                    m.UserId == userId &&
                    (
                        m.Role == OrganisationRole.Owner ||
                        m.Role == OrganisationRole.Administrator ||
                        m.Role == OrganisationRole.Accountant ||
                        m.Role == OrganisationRole.Bookkeeper
                    )));
}
}
