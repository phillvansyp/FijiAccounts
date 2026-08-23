using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record AccessibleOrganisation(Organisation Organisation, string AccessLabel, bool IsClient);

public sealed class TenantAccessService(ApplicationDbContext db)
{
    public async Task<List<AccessibleOrganisation>> ListAsync(string userId)
    {
        var direct = await db.OrganisationMemberships.AsNoTracking().Include(x => x.Organisation)
            .Where(x => x.UserId == userId &&
                (x.Organisation.OrganisationGroupId == null ||
                 x.Organisation.OrganisationGroup!.Status == TenantStatus.Active))
            .Select(x => new AccessibleOrganisation(x.Organisation, x.Role.ToString(), false)).ToListAsync();

        var practiceIds = await db.OrganisationMemberships.AsNoTracking()
            .Where(x => x.UserId == userId && x.Organisation.Kind == OrganisationKind.AccountingPractice &&
                (x.Organisation.OrganisationGroupId == null ||
                 x.Organisation.OrganisationGroup!.Status == TenantStatus.Active) &&
                (x.Role == OrganisationRole.Owner || x.Role == OrganisationRole.Administrator ||
                 x.Role == OrganisationRole.Accountant || x.Role == OrganisationRole.Bookkeeper))
            .Select(x => x.OrganisationId).ToArrayAsync();
        var clients = await db.AccountantEngagements.AsNoTracking().Include(x => x.ClientOrganisation)
            .Where(x => practiceIds.Contains(x.PracticeOrganisationId) && x.RevokedAt == null &&
                (x.ClientOrganisation.OrganisationGroupId == null ||
                 x.ClientOrganisation.OrganisationGroup!.Status == TenantStatus.Active))
            .Select(x => new AccessibleOrganisation(x.ClientOrganisation, x.Access.ToString(), true)).ToListAsync();

        return direct.Concat(clients).GroupBy(x => x.Organisation.Id).Select(x => x.First())
            .OrderBy(x => x.Organisation.LegalName).ToList();
    }

    public async Task<AccessibleOrganisation?> FindAsync(string userId, Guid organisationId) =>
        (await ListAsync(userId)).SingleOrDefault(x => x.Organisation.Id == organisationId);

    public async Task<bool> CanManageTeamAsync(string userId, Guid organisationId) =>
        await db.OrganisationMemberships.AnyAsync(x => x.UserId == userId && x.OrganisationId == organisationId &&
            (x.Organisation.OrganisationGroupId == null || x.Organisation.OrganisationGroup!.Status == TenantStatus.Active) &&
            (x.Role == OrganisationRole.Owner || x.Role == OrganisationRole.Administrator));

    public async Task<List<Branch>> ListAccessibleBranchesAsync(
        string userId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        if (await FindAsync(userId, organisationId) is null)
        {
            return [];
        }

        var branches = await db.Branches
            .AsNoTracking()
            .Include(x => x.Divisions.Where(division => division.IsActive))
            .Where(x => x.OrganisationId == organisationId && x.IsActive)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Code)
            .ToListAsync(cancellationToken);
        var membership = await db.OrganisationMemberships
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.OrganisationId == organisationId && x.UserId == userId,
                cancellationToken);

        if (membership is null ||
            membership.Role is OrganisationRole.Owner or OrganisationRole.Administrator ||
            membership.DimensionAccessMode == DimensionAccessMode.All)
        {
            return branches;
        }

        var grants = await db.OrganisationDimensionAccessGrants
            .AsNoTracking()
            .Where(x => x.OrganisationId == organisationId && x.UserId == userId)
            .ToListAsync(cancellationToken);
        var branchWide = grants.Where(x => x.DivisionId == null).Select(x => x.BranchId).ToHashSet();
        var divisionIds = grants.Where(x => x.DivisionId != null).Select(x => x.DivisionId!.Value).ToHashSet();

        foreach (var branch in branches)
        {
            if (!branchWide.Contains(branch.Id))
            {
                branch.Divisions = branch.Divisions.Where(x => divisionIds.Contains(x.Id)).ToList();
            }
        }

        return branches.Where(x => branchWide.Contains(x.Id) || x.Divisions.Count > 0).ToList();
    }

    public async Task<bool> CanAccessDimensionAsync(
        string userId,
        Guid organisationId,
        Guid branchId,
        Guid divisionId,
        CancellationToken cancellationToken = default)
    {
        if (await FindAsync(userId, organisationId) is null)
        {
            return false;
        }

        var membership = await db.OrganisationMemberships
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.OrganisationId == organisationId && x.UserId == userId,
                cancellationToken);
        if (membership is null)
        {
            return true;
        }

        if (membership.Role is OrganisationRole.Owner or OrganisationRole.Administrator ||
            membership.DimensionAccessMode == DimensionAccessMode.All)
        {
            return true;
        }

        return await db.OrganisationDimensionAccessGrants.AnyAsync(
            x => x.OrganisationId == organisationId &&
                 x.UserId == userId &&
                 x.BranchId == branchId &&
                 (x.DivisionId == null || x.DivisionId == divisionId),
            cancellationToken);
    }

    public async Task<Guid[]> ListAccessibleDivisionIdsAsync(
        string userId,
        Guid organisationId,
        CancellationToken cancellationToken = default) =>
        (await ListAccessibleBranchesAsync(userId, organisationId, cancellationToken))
            .SelectMany(x => x.Divisions)
            .Select(x => x.Id)
            .ToArray();

    public async Task<Guid[]?> GetReportDivisionScopeAsync(
        string userId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        if (await FindAsync(userId, organisationId) is null)
        {
            return [];
        }

        var membership = await db.OrganisationMemberships
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.OrganisationId == organisationId && x.UserId == userId,
                cancellationToken);
        if (membership is null ||
            membership.Role is OrganisationRole.Owner or OrganisationRole.Administrator ||
            membership.DimensionAccessMode == DimensionAccessMode.All)
        {
            return null;
        }

        var grants = await db.OrganisationDimensionAccessGrants
            .AsNoTracking()
            .Where(x => x.OrganisationId == organisationId && x.UserId == userId)
            .ToListAsync(cancellationToken);
        var branchIds = grants.Where(x => x.DivisionId == null).Select(x => x.BranchId).ToArray();
        var explicitDivisionIds = grants.Where(x => x.DivisionId != null).Select(x => x.DivisionId!.Value);
        var branchDivisionIds = await db.Divisions
            .AsNoTracking()
            .Where(x => branchIds.Contains(x.BranchId))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        return explicitDivisionIds.Concat(branchDivisionIds).Distinct().ToArray();
    }

    public async Task SetDimensionAccessModeAsync(
        string actorUserId,
        Guid organisationId,
        string memberUserId,
        DimensionAccessMode mode,
        CancellationToken cancellationToken = default)
    {
        if (!await CanManageTeamAsync(actorUserId, organisationId))
        {
            throw new UnauthorizedAccessException("You cannot manage this organisation's team.");
        }

        var membership = await db.OrganisationMemberships.SingleOrDefaultAsync(
            x => x.OrganisationId == organisationId && x.UserId == memberUserId,
            cancellationToken) ?? throw new InvalidOperationException("The selected user is not an organisation member.");
        if (mode == DimensionAccessMode.Restricted &&
            membership.Role is OrganisationRole.Owner or OrganisationRole.Administrator)
        {
            throw new InvalidOperationException("Owners and administrators must retain access to all dimensions.");
        }

        var previousMode = membership.DimensionAccessMode;
        membership.DimensionAccessMode = mode;
        var removedGrantCount = 0;
        if (mode == DimensionAccessMode.All)
        {
            var grants = await db.OrganisationDimensionAccessGrants
                .Where(x => x.OrganisationId == organisationId && x.UserId == memberUserId)
                .ToListAsync(cancellationToken);
            removedGrantCount = grants.Count;
            db.OrganisationDimensionAccessGrants.RemoveRange(grants);
        }

        if (previousMode == mode && removedGrantCount == 0)
        {
            return;
        }

        db.AuditEvents.Add(AccessAudit(
            organisationId,
            actorUserId,
            "DimensionAccessModeChanged",
            nameof(OrganisationMembership),
            MembershipEntityId(organisationId, memberUserId),
            new
            {
                MemberUserId = memberUserId,
                OldMode = previousMode.ToString(),
                NewMode = mode.ToString(),
                RemovedGrants = removedGrantCount
            }));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddDimensionAccessGrantAsync(
        string actorUserId,
        Guid organisationId,
        string memberUserId,
        Guid branchId,
        Guid? divisionId,
        CancellationToken cancellationToken = default)
    {
        if (!await CanManageTeamAsync(actorUserId, organisationId))
        {
            throw new UnauthorizedAccessException("You cannot manage this organisation's team.");
        }

        var membership = await db.OrganisationMemberships.SingleOrDefaultAsync(
            x => x.OrganisationId == organisationId && x.UserId == memberUserId,
            cancellationToken) ?? throw new InvalidOperationException("The selected user is not an organisation member.");
        if (membership.Role is OrganisationRole.Owner or OrganisationRole.Administrator)
        {
            throw new InvalidOperationException("Owners and administrators already have access to all dimensions.");
        }

        var branch = await db.Branches
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == branchId && x.OrganisationId == organisationId && x.IsActive,
                cancellationToken) ?? throw new InvalidOperationException("The selected branch is not active in this organisation.");
        if (divisionId is Guid selectedDivisionId &&
            !await db.Divisions.AnyAsync(
                x => x.Id == selectedDivisionId && x.BranchId == branch.Id && x.IsActive,
                cancellationToken))
        {
            throw new InvalidOperationException("The selected division is not active in this branch.");
        }

        var previousMode = membership.DimensionAccessMode;
        var existingGrant = await db.OrganisationDimensionAccessGrants.SingleOrDefaultAsync(
            x => x.OrganisationId == organisationId && x.UserId == memberUserId &&
                 x.BranchId == branchId && x.DivisionId == divisionId,
            cancellationToken);
        membership.DimensionAccessMode = DimensionAccessMode.Restricted;
        if (previousMode != DimensionAccessMode.Restricted)
        {
            db.AuditEvents.Add(AccessAudit(
                organisationId,
                actorUserId,
                "DimensionAccessModeChanged",
                nameof(OrganisationMembership),
                MembershipEntityId(organisationId, memberUserId),
                new
                {
                    MemberUserId = memberUserId,
                    OldMode = previousMode.ToString(),
                    NewMode = DimensionAccessMode.Restricted.ToString(),
                    RemovedGrants = 0
                }));
        }

        if (existingGrant is null)
        {
            var grant = new OrganisationDimensionAccessGrant
            {
                OrganisationId = organisationId,
                UserId = memberUserId,
                BranchId = branchId,
                DivisionId = divisionId
            };
            db.OrganisationDimensionAccessGrants.Add(grant);
            db.AuditEvents.Add(AccessAudit(
                organisationId,
                actorUserId,
                "DimensionAccessGrantAdded",
                nameof(OrganisationDimensionAccessGrant),
                grant.Id.ToString(),
                new
                {
                    MemberUserId = memberUserId,
                    grant.BranchId,
                    grant.DivisionId
                }));
        }

        if (previousMode == DimensionAccessMode.Restricted && existingGrant is not null)
        {
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveDimensionAccessGrantAsync(
        string actorUserId,
        Guid organisationId,
        Guid grantId,
        CancellationToken cancellationToken = default)
    {
        if (!await CanManageTeamAsync(actorUserId, organisationId))
        {
            throw new UnauthorizedAccessException("You cannot manage this organisation's team.");
        }

        var grant = await db.OrganisationDimensionAccessGrants.SingleOrDefaultAsync(
            x => x.Id == grantId && x.OrganisationId == organisationId,
            cancellationToken) ?? throw new InvalidOperationException("The selected access grant was not found.");
        db.AuditEvents.Add(AccessAudit(
            organisationId,
            actorUserId,
            "DimensionAccessGrantRemoved",
            nameof(OrganisationDimensionAccessGrant),
            grant.Id.ToString(),
            new
            {
                MemberUserId = grant.UserId,
                grant.BranchId,
                grant.DivisionId
            }));
        db.OrganisationDimensionAccessGrants.Remove(grant);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string MembershipEntityId(
        Guid organisationId,
        string memberUserId) =>
        $"{organisationId}:{memberUserId}";

    private static AuditEvent AccessAudit(
        Guid organisationId,
        string actorUserId,
        string eventType,
        string entityType,
        string entityId,
        object evidence) =>
        new()
        {
            OrganisationId = organisationId,
            UserId = actorUserId,
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId,
            JsonData = JsonSerializer.Serialize(evidence)
        };

    public async Task<bool> CanPostJournalsAsync(string userId, Guid organisationId)
    {
        if (await db.OrganisationMemberships.AnyAsync(x => x.UserId == userId && x.OrganisationId == organisationId &&
            (x.Organisation.OrganisationGroupId == null || x.Organisation.OrganisationGroup!.Status == TenantStatus.Active) &&
            (x.Role == OrganisationRole.Owner || x.Role == OrganisationRole.Administrator || x.Role == OrganisationRole.Accountant || x.Role == OrganisationRole.Bookkeeper))) return true;

        return await db.AccountantEngagements.AnyAsync(e => e.ClientOrganisationId == organisationId && e.RevokedAt == null &&
            (e.ClientOrganisation.OrganisationGroupId == null || e.ClientOrganisation.OrganisationGroup!.Status == TenantStatus.Active) &&
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
            (x.Organisation.OrganisationGroupId == null ||
             x.Organisation.OrganisationGroup!.Status == TenantStatus.Active) &&
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
            (e.ClientOrganisation.OrganisationGroupId == null ||
             e.ClientOrganisation.OrganisationGroup!.Status == TenantStatus.Active) &&
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
