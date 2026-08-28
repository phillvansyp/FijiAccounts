using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record SaveOrganisationPermissionProfileRequest(
    string Name,
    string? Description,
    bool CanManageTeam,
    bool CanPostAccounting,
    bool CanManageContacts,
    bool CanApprovePurchases);

public sealed class OrganisationPermissionProfileService(
    ApplicationDbContext db,
    TenantAccessService access)
{
    public async Task<List<OrganisationPermissionProfile>> ListAsync(
        string actorUserId,
        Guid organisationId,
        CancellationToken ct = default)
    {
        await RequireManagerAsync(actorUserId, organisationId, ct);
        return await db.OrganisationPermissionProfiles.AsNoTracking()
            .Where(x => x.OrganisationId == organisationId)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
    }

    public async Task<OrganisationPermissionProfile> CreateAsync(
        string actorUserId,
        Guid organisationId,
        SaveOrganisationPermissionProfileRequest request,
        CancellationToken ct = default)
    {
        await RequireManagerAsync(actorUserId, organisationId, ct);
        var name = ValidateName(request.Name);
        if (await db.OrganisationPermissionProfiles.AnyAsync(
                x => x.OrganisationId == organisationId && x.Name == name, ct))
            throw new InvalidOperationException("A permission profile with this name already exists.");

        var profile = new OrganisationPermissionProfile
        {
            OrganisationId = organisationId,
            Name = name,
            Description = CleanDescription(request.Description),
            CanManageTeam = request.CanManageTeam,
            CanPostAccounting = request.CanPostAccounting,
            CanManageContacts = request.CanManageContacts,
            CanApprovePurchases = request.CanApprovePurchases,
            CreatedByUserId = actorUserId
        };
        db.OrganisationPermissionProfiles.Add(profile);
        AddAudit(actorUserId, organisationId, "PermissionProfileCreated", profile);
        await db.SaveChangesAsync(ct);
        return profile;
    }

    public async Task<OrganisationPermissionProfile> UpdateAsync(
        string actorUserId,
        Guid organisationId,
        Guid profileId,
        SaveOrganisationPermissionProfileRequest request,
        CancellationToken ct = default)
    {
        await RequireManagerAsync(actorUserId, organisationId, ct);
        var profile = await db.OrganisationPermissionProfiles.SingleOrDefaultAsync(
            x => x.Id == profileId && x.OrganisationId == organisationId, ct)
            ?? throw new InvalidOperationException("Permission profile not found.");
        var name = ValidateName(request.Name);
        if (await db.OrganisationPermissionProfiles.AnyAsync(
                x => x.OrganisationId == organisationId && x.Id != profileId && x.Name == name, ct))
            throw new InvalidOperationException("A permission profile with this name already exists.");

        profile.Name = name;
        profile.Description = CleanDescription(request.Description);
        profile.CanManageTeam = request.CanManageTeam;
        profile.CanPostAccounting = request.CanPostAccounting;
        profile.CanManageContacts = request.CanManageContacts;
        profile.CanApprovePurchases = request.CanApprovePurchases;
        AddAudit(actorUserId, organisationId, "PermissionProfileUpdated", profile);
        await db.SaveChangesAsync(ct);
        return profile;
    }

    public async Task AssignAsync(
        string actorUserId,
        Guid organisationId,
        string memberUserId,
        Guid? profileId,
        CancellationToken ct = default)
    {
        await RequireManagerAsync(actorUserId, organisationId, ct);
        var member = await db.OrganisationMemberships.SingleOrDefaultAsync(
            x => x.OrganisationId == organisationId && x.UserId == memberUserId, ct)
            ?? throw new InvalidOperationException("Team member not found.");
        if (member.Role == OrganisationRole.Owner)
            throw new InvalidOperationException("The Owner profile is protected and cannot be replaced.");
        if (actorUserId == memberUserId)
            throw new InvalidOperationException("You cannot change your own permission profile.");
        if (profileId is not null && !await db.OrganisationPermissionProfiles.AnyAsync(
                x => x.Id == profileId && x.OrganisationId == organisationId, ct))
            throw new InvalidOperationException("Permission profile not found.");

        member.PermissionProfileId = profileId;
        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = organisationId,
            UserId = actorUserId,
            EventType = "MemberPermissionProfileAssigned",
            EntityType = nameof(OrganisationMembership),
            EntityId = memberUserId,
            JsonData = JsonSerializer.Serialize(new { MemberUserId = memberUserId, PermissionProfileId = profileId })
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(
        string actorUserId,
        Guid organisationId,
        Guid profileId,
        CancellationToken ct = default)
    {
        await RequireManagerAsync(actorUserId, organisationId, ct);
        var profile = await db.OrganisationPermissionProfiles.SingleOrDefaultAsync(
            x => x.Id == profileId && x.OrganisationId == organisationId, ct)
            ?? throw new InvalidOperationException("Permission profile not found.");
        if (await db.OrganisationMemberships.AnyAsync(x => x.PermissionProfileId == profileId, ct))
            throw new InvalidOperationException("Reassign members before deleting this permission profile.");
        AddAudit(actorUserId, organisationId, "PermissionProfileDeleted", profile);
        db.OrganisationPermissionProfiles.Remove(profile);
        await db.SaveChangesAsync(ct);
    }

    private async Task RequireManagerAsync(string userId, Guid organisationId, CancellationToken ct)
    {
        if (!await access.CanManageTeamAsync(userId, organisationId))
            throw new UnauthorizedAccessException("You cannot manage this organisation's permission profiles.");
    }

    private static string ValidateName(string name)
    {
        var value = name.Trim();
        if (value.Length is < 2 or > 100)
            throw new InvalidOperationException("Enter a profile name between 2 and 100 characters.");
        return value;
    }

    private static string? CleanDescription(string? value)
    {
        var result = value?.Trim();
        if (result?.Length > 500) throw new InvalidOperationException("The description cannot exceed 500 characters.");
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private void AddAudit(string actorUserId, Guid organisationId, string eventType, OrganisationPermissionProfile profile) =>
        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = organisationId,
            UserId = actorUserId,
            EventType = eventType,
            EntityType = nameof(OrganisationPermissionProfile),
            EntityId = profile.Id.ToString(),
            JsonData = JsonSerializer.Serialize(new
            {
                profile.Name,
                profile.CanManageTeam,
                profile.CanPostAccounting,
                profile.CanManageContacts,
                profile.CanApprovePurchases
            })
        });
}
