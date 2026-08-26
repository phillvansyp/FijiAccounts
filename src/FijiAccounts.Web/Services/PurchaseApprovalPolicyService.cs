using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record PurchaseApprovalPolicyRequest(
    Guid OrganisationId,
    string Name,
    decimal MinimumAmount,
    decimal? MaximumAmount,
    PurchaseApprovalRequirement Requirement,
    Guid? BranchId = null,
    Guid? DivisionId = null);

public sealed class PurchaseApprovalPolicyService(
    ApplicationDbContext db,
    TenantAccessService access)
{
    public async Task<List<PurchaseApprovalPolicy>> ListAsync(
        string userId,
        Guid organisationId,
        CancellationToken ct = default)
    {
        await RequireManagerAsync(userId, organisationId);
        return await db.PurchaseApprovalPolicies.AsNoTracking()
            .Include(x => x.Branch)
            .Include(x => x.Division)
            .Where(x => x.OrganisationId == organisationId)
            .OrderByDescending(x => x.DivisionId != null)
            .ThenByDescending(x => x.BranchId != null)
            .ThenBy(x => x.MinimumAmount)
            .ToListAsync(ct);
    }

    public async Task<PurchaseApprovalPolicy> CreateAsync(
        string userId,
        PurchaseApprovalPolicyRequest request,
        CancellationToken ct = default)
    {
        await RequireManagerAsync(userId, request.OrganisationId);
        var name = request.Name.Trim();
        if (name.Length is 0 or > 120 || request.MinimumAmount < 0 ||
            request.MaximumAmount < request.MinimumAmount || !Enum.IsDefined(request.Requirement) ||
            request.DivisionId != null && request.BranchId == null)
        {
            throw new InvalidOperationException("Enter a valid approval policy name, amount range, scope and requirement.");
        }

        if (request.BranchId != null)
        {
            var scopeExists = await db.Branches.AnyAsync(x =>
                x.Id == request.BranchId && x.OrganisationId == request.OrganisationId && x.IsActive &&
                (request.DivisionId == null || x.Divisions.Any(d => d.Id == request.DivisionId && d.IsActive)), ct);
            if (!scopeExists)
            {
                throw new InvalidOperationException("Select an active branch and division from this organisation.");
            }
        }

        var sameScope = await db.PurchaseApprovalPolicies.AsNoTracking().Where(x =>
            x.OrganisationId == request.OrganisationId && x.IsActive &&
            x.BranchId == request.BranchId && x.DivisionId == request.DivisionId).ToListAsync(ct);
        var overlaps = sameScope.Any(x =>
            request.MinimumAmount <= (x.MaximumAmount ?? decimal.MaxValue) &&
            x.MinimumAmount <= (request.MaximumAmount ?? decimal.MaxValue));
        if (overlaps)
        {
            throw new InvalidOperationException("Approval policy amount ranges cannot overlap within the same scope.");
        }

        var policy = new PurchaseApprovalPolicy
        {
            OrganisationId = request.OrganisationId,
            BranchId = request.BranchId,
            DivisionId = request.DivisionId,
            Name = name,
            MinimumAmount = request.MinimumAmount,
            MaximumAmount = request.MaximumAmount,
            Requirement = request.Requirement,
            CreatedByUserId = userId
        };
        db.PurchaseApprovalPolicies.Add(policy);
        db.AuditEvents.Add(Audit(policy, userId, "PurchaseApprovalPolicyCreated"));
        await db.SaveChangesAsync(ct);
        return policy;
    }

    public async Task DeleteAsync(
        string userId,
        Guid organisationId,
        Guid policyId,
        CancellationToken ct = default)
    {
        await RequireManagerAsync(userId, organisationId);
        var policy = await db.PurchaseApprovalPolicies.SingleOrDefaultAsync(
            x => x.Id == policyId && x.OrganisationId == organisationId, ct)
            ?? throw new InvalidOperationException("Approval policy not found.");
        db.PurchaseApprovalPolicies.Remove(policy);
        db.AuditEvents.Add(Audit(policy, userId, "PurchaseApprovalPolicyDeleted"));
        await db.SaveChangesAsync(ct);
    }

    public async Task<PurchaseApprovalPolicy?> ResolveAsync(
        Guid organisationId,
        Guid branchId,
        Guid divisionId,
        decimal amount,
        CancellationToken ct = default)
    {
        var candidates = await db.PurchaseApprovalPolicies.AsNoTracking().Where(x =>
            x.OrganisationId == organisationId && x.IsActive &&
            x.MinimumAmount <= amount && (x.MaximumAmount == null || amount <= x.MaximumAmount) &&
            (x.BranchId == null || x.BranchId == branchId) &&
            (x.DivisionId == null || x.DivisionId == divisionId)).ToListAsync(ct);
        return candidates
            .OrderByDescending(x => x.DivisionId != null)
            .ThenByDescending(x => x.BranchId != null)
            .ThenByDescending(x => x.MinimumAmount)
            .ThenBy(x => x.Id)
            .FirstOrDefault();
    }

    public async Task<bool> CanApproveAsync(
        string userId,
        Guid organisationId,
        PurchaseApprovalRequirement requirement,
        CancellationToken ct = default)
    {
        if (!Enum.IsDefined(requirement)) return false;
        return await db.OrganisationMemberships.AnyAsync(x =>
            x.UserId == userId && x.OrganisationId == organisationId &&
            (x.Organisation.OrganisationGroupId == null || x.Organisation.OrganisationGroup!.Status == TenantStatus.Active) &&
            (requirement == PurchaseApprovalRequirement.OwnerOnly
                ? x.Role == OrganisationRole.Owner
                : x.Role == OrganisationRole.Owner ||
                  x.Role == OrganisationRole.Administrator ||
                  x.Role == OrganisationRole.Approver), ct);
    }

    private async Task RequireManagerAsync(string userId, Guid organisationId)
    {
        if (!await access.CanManageTeamAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException("Only an owner or administrator can manage purchase approval policies.");
        }
    }

    private static AuditEvent Audit(PurchaseApprovalPolicy policy, string userId, string eventType) => new()
    {
        OrganisationId = policy.OrganisationId,
        UserId = userId,
        EventType = eventType,
        EntityType = nameof(PurchaseApprovalPolicy),
        EntityId = policy.Id.ToString(),
        JsonData = JsonSerializer.Serialize(new
        {
            policy.Name,
            policy.BranchId,
            policy.DivisionId,
            policy.MinimumAmount,
            policy.MaximumAmount,
            Requirement = policy.Requirement.ToString(),
            policy.IsActive
        })
    };
}
