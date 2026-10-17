using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

// The selected company belongs to the user, not to a page or a browser circuit.
public sealed class OrganisationSelectionService(ApplicationDbContext db, TenantAccessService tenantAccess)
{
    public async Task<Guid?> GetAsync(string userId)
        => await db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.LastSelectedOrganisationId)
            .SingleOrDefaultAsync();

    public async Task SelectAsync(string userId, Guid organisationId)
    {
        if (await tenantAccess.FindAsync(userId, organisationId) is null)
        {
            throw new UnauthorizedAccessException("This company is not available to the signed-in user.");
        }

        await db.Users.Where(user => user.Id == userId && user.LastSelectedOrganisationId != organisationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.LastSelectedOrganisationId, organisationId));
    }
}
