using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record AccountantReviewWorkItem(
    Guid OrganisationId,
    string OrganisationName,
    Guid PeriodId,
    string PeriodName,
    YearEndReviewArea Area,
    string Query,
    string? AssignedToUserId,
    string AssignedToLabel,
    DateOnly? DueDate,
    DateTimeOffset? RaisedAt,
    string? Response,
    DateTimeOffset? RespondedAt,
    DateTimeOffset? ResolvedAt,
    int AttachmentCount,
    bool CanManage,
    bool IsAssignedToCurrentUser)
{
    public bool IsResolved => ResolvedAt is not null;
    public bool HasResponse => !string.IsNullOrWhiteSpace(Response);
    public bool IsOverdue(DateOnly today) =>
        !IsResolved && !HasResponse && DueDate is not null && DueDate < today;
}

public sealed class AccountantReviewWorklistService(
    ApplicationDbContext db,
    TenantAccessService access)
{
    public async Task<List<AccountantReviewWorkItem>> GetAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var accessible = await access.ListAsync(userId);
        var accessibleOrganisationIds = accessible
            .Select(x => x.Organisation.Id)
            .Distinct()
            .ToArray();
        if (accessibleOrganisationIds.Length == 0)
        {
            return [];
        }

        var manageableOrganisationIds = await db.OrganisationMemberships.AsNoTracking()
            .Where(x => x.UserId == userId &&
                        accessibleOrganisationIds.Contains(x.OrganisationId) &&
                        (x.Role == OrganisationRole.Owner ||
                         (x.PermissionProfileId != null
                             ? x.PermissionProfile!.CanManageTeam
                             : x.Role == OrganisationRole.Administrator)))
            .Select(x => x.OrganisationId)
            .ToArrayAsync(cancellationToken);

        var rows = await db.YearEndReviewItems.AsNoTracking()
            .Where(x =>
                accessibleOrganisationIds.Contains(x.YearEndReview.OrganisationId) &&
                x.QueryRaisedAt != null &&
                (manageableOrganisationIds.Contains(x.YearEndReview.OrganisationId) ||
                 x.QueryAssignedToUserId == userId))
            .Select(x => new
            {
                x.YearEndReview.OrganisationId,
                OrganisationName = x.YearEndReview.AccountingPeriod.Organisation.LegalName,
                PeriodId = x.YearEndReview.AccountingPeriodId,
                PeriodName = x.YearEndReview.AccountingPeriod.Name,
                x.Area,
                Query = x.Notes ?? "",
                AssignedToUserId = x.QueryAssignedToUserId,
                x.QueryDueDate,
                x.QueryRaisedAt,
                x.QueryResponse,
                x.QueryRespondedAt,
                x.QueryResolvedAt,
                AttachmentCount = x.Attachments.Count
            })
            .ToListAsync(cancellationToken);

        var assigneeIds = rows
            .Select(x => x.AssignedToUserId)
            .Where(x => x is not null)
            .Distinct()
            .ToArray();
        var assigneeLabels = await db.Users.AsNoTracking()
            .Where(x => assigneeIds.Contains(x.Id))
            .ToDictionaryAsync(
                x => x.Id,
                x => x.Email ?? x.UserName ?? x.Id,
                cancellationToken);
        var manageable = manageableOrganisationIds.ToHashSet();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return rows
            .Select(x => new AccountantReviewWorkItem(
                x.OrganisationId,
                x.OrganisationName,
                x.PeriodId,
                x.PeriodName,
                x.Area,
                x.Query,
                x.AssignedToUserId,
                x.AssignedToUserId is not null &&
                assigneeLabels.TryGetValue(x.AssignedToUserId, out var label)
                    ? label
                    : x.AssignedToUserId ?? "Unassigned",
                x.QueryDueDate,
                x.QueryRaisedAt,
                x.QueryResponse,
                x.QueryRespondedAt,
                x.QueryResolvedAt,
                x.AttachmentCount,
                manageable.Contains(x.OrganisationId),
                x.AssignedToUserId == userId))
            .OrderBy(x => x.IsResolved)
            .ThenByDescending(x => x.IsOverdue(today))
            .ThenByDescending(x => x.HasResponse)
            .ThenBy(x => x.DueDate)
            .ThenBy(x => x.OrganisationName)
            .ThenBy(x => x.Area)
            .ToList();
    }
}
