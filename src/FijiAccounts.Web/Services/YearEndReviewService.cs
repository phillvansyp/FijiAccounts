using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record YearEndReviewAssignee(string UserId, string Label);

public sealed class YearEndReviewService(
    ApplicationDbContext db,
    TenantAccessService access)
{
    public async Task<YearEndReview?> GetAsync(
        string userId,
        Guid organisationId,
        Guid periodId,
        CancellationToken cancellationToken = default)
    {
        if (!await CanViewAsync(userId, organisationId, periodId, cancellationToken))
        {
            throw new UnauthorizedAccessException(
                "You cannot access this year-end review.");
        }

        return await db.YearEndReviews.AsNoTracking()
            .Include(x => x.Items)
            .ThenInclude(x => x.Attachments)
            .SingleOrDefaultAsync(
                x => x.OrganisationId == organisationId &&
                     x.AccountingPeriodId == periodId,
                cancellationToken);
    }

    public async Task<List<YearEndReviewAssignee>> ListAssigneesAsync(
        string userId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureCanManageAsync(userId, organisationId);
        var directUserIds = await db.OrganisationMemberships.AsNoTracking()
            .Where(x => x.OrganisationId == organisationId)
            .Select(x => x.UserId)
            .ToListAsync(cancellationToken);
        var practiceIds = await db.AccountantEngagements.AsNoTracking()
            .Where(x => x.ClientOrganisationId == organisationId && x.RevokedAt == null)
            .Select(x => x.PracticeOrganisationId)
            .ToListAsync(cancellationToken);
        var practiceUserIds = await db.OrganisationMemberships.AsNoTracking()
            .Where(x => practiceIds.Contains(x.OrganisationId))
            .Select(x => x.UserId)
            .ToListAsync(cancellationToken);
        var candidateIds = directUserIds.Concat(practiceUserIds).Distinct().ToArray();
        return await db.Users.AsNoTracking()
            .Where(x => candidateIds.Contains(x.Id))
            .OrderBy(x => x.Email ?? x.UserName)
            .Select(x => new YearEndReviewAssignee(
                x.Id,
                x.Email ?? x.UserName ?? x.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<YearEndReview> StartAsync(
        string userId,
        Guid organisationId,
        Guid periodId,
        CancellationToken cancellationToken = default)
    {
        await EnsureCanManageAsync(userId, organisationId);

        var period = await db.AccountingPeriods
            .SingleOrDefaultAsync(
                x => x.Id == periodId && x.OrganisationId == organisationId,
                cancellationToken)
            ?? throw new InvalidOperationException("Accounting period not found.");
        if (period.IsLocked)
        {
            throw new InvalidOperationException(
                "Unlock the accounting period before starting its year-end review.");
        }

        var existing = await db.YearEndReviews
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.AccountingPeriodId == periodId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var review = new YearEndReview
        {
            OrganisationId = organisationId,
            AccountingPeriodId = periodId,
            StartedByUserId = userId,
            Items = Enum.GetValues<YearEndReviewArea>()
                .Select(area => new YearEndReviewItem { Area = area })
                .ToList()
        };
        db.YearEndReviews.Add(review);
        db.AuditEvents.Add(Audit(
            organisationId,
            userId,
            "YearEndReviewStarted",
            periodId,
            new { period.Name, period.StartsOn, period.EndsOn }));
        await db.SaveChangesAsync(cancellationToken);
        return review;
    }

    public async Task<YearEndReview> UpdateItemAsync(
        string userId,
        Guid organisationId,
        Guid periodId,
        YearEndReviewArea area,
        YearEndReviewStatus status,
        string? notes,
        string? assignedToUserId = null,
        DateOnly? dueDate = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureCanManageAsync(userId, organisationId);
        var review = await LoadForChangeAsync(organisationId, periodId, cancellationToken);
        EnsureOpen(review);

        var trimmedNotes = notes?.Trim();
        if (trimmedNotes?.Length > 500)
        {
            throw new InvalidOperationException("Review notes cannot exceed 500 characters.");
        }

        if (status == YearEndReviewStatus.QueryRaised && string.IsNullOrWhiteSpace(trimmedNotes))
        {
            throw new InvalidOperationException("Enter the outstanding query in the review notes.");
        }

        var item = review.Items.Single(x => x.Area == area);
        if (item.Status == YearEndReviewStatus.QueryRaised &&
            status != YearEndReviewStatus.QueryRaised)
        {
            throw new InvalidOperationException(
                "Respond to and resolve the outstanding query before clearing this review item.");
        }

        if (status == YearEndReviewStatus.QueryRaised)
        {
            if (string.IsNullOrWhiteSpace(assignedToUserId) || dueDate is null)
            {
                throw new InvalidOperationException(
                    "Assign the query and enter its due date.");
            }

            if (!await IsAssigneeCandidateAsync(
                    assignedToUserId,
                    organisationId,
                    cancellationToken))
            {
                throw new InvalidOperationException(
                    "The query owner must have access to this organisation or its accounting engagement.");
            }

            var resetResponse = item.Status != YearEndReviewStatus.QueryRaised ||
                                item.QueryAssignedToUserId != assignedToUserId;
            item.QueryAssignedToUserId = assignedToUserId;
            item.QueryDueDate = dueDate;
            item.QueryRaisedAt ??= DateTimeOffset.UtcNow;
            item.QueryRaisedByUserId ??= userId;
            if (resetResponse)
            {
                item.QueryResponse = null;
                item.QueryRespondedAt = null;
                item.QueryRespondedByUserId = null;
            }

            item.QueryResolvedAt = null;
            item.QueryResolvedByUserId = null;
        }
        else if (status != YearEndReviewStatus.Reviewed || item.QueryResolvedAt is null)
        {
            item.QueryAssignedToUserId = null;
            item.QueryDueDate = null;
            item.QueryRaisedAt = null;
            item.QueryRaisedByUserId = null;
            item.QueryResponse = null;
            item.QueryRespondedAt = null;
            item.QueryRespondedByUserId = null;
            item.QueryResolvedAt = null;
            item.QueryResolvedByUserId = null;
        }

        item.Status = status;
        item.Notes = trimmedNotes;
        item.ReviewedAt = status == YearEndReviewStatus.Reviewed
            ? DateTimeOffset.UtcNow
            : null;
        item.ReviewedByUserId = status == YearEndReviewStatus.Reviewed
            ? userId
            : null;

        db.AuditEvents.Add(Audit(
            organisationId,
            userId,
            "YearEndReviewItemUpdated",
            periodId,
            new
            {
                Area = area,
                Status = status,
                Notes = trimmedNotes,
                item.QueryAssignedToUserId,
                item.QueryDueDate
            }));
        await db.SaveChangesAsync(cancellationToken);
        return review;
    }

    public async Task<YearEndReview> RespondAsync(
        string userId,
        Guid organisationId,
        Guid periodId,
        YearEndReviewArea area,
        string response,
        CancellationToken cancellationToken = default)
    {
        var review = await LoadForChangeAsync(organisationId, periodId, cancellationToken);
        EnsureOpen(review);
        var item = review.Items.Single(x => x.Area == area);
        var canManage = await access.CanManageTeamAsync(userId, organisationId);
        if (!canManage && item.QueryAssignedToUserId != userId)
        {
            throw new UnauthorizedAccessException(
                "Only the assigned query owner or an organisation administrator can respond.");
        }

        if (item.Status != YearEndReviewStatus.QueryRaised)
        {
            throw new InvalidOperationException("This review item has no outstanding query.");
        }

        var trimmedResponse = response?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedResponse))
        {
            throw new InvalidOperationException("Enter a response to the review query.");
        }

        if (trimmedResponse.Length > 1000)
        {
            throw new InvalidOperationException("The query response cannot exceed 1000 characters.");
        }

        item.QueryResponse = trimmedResponse;
        item.QueryRespondedAt = DateTimeOffset.UtcNow;
        item.QueryRespondedByUserId = userId;
        db.AuditEvents.Add(Audit(
            organisationId,
            userId,
            "YearEndReviewQueryResponded",
            periodId,
            new { Area = area, Response = trimmedResponse }));
        await db.SaveChangesAsync(cancellationToken);
        return review;
    }

    public async Task<YearEndReview> ResolveQueryAsync(
        string userId,
        Guid organisationId,
        Guid periodId,
        YearEndReviewArea area,
        CancellationToken cancellationToken = default)
    {
        await EnsureCanManageAsync(userId, organisationId);
        var review = await LoadForChangeAsync(organisationId, periodId, cancellationToken);
        EnsureOpen(review);
        var item = review.Items.Single(x => x.Area == area);
        if (item.Status != YearEndReviewStatus.QueryRaised)
        {
            throw new InvalidOperationException("This review item has no outstanding query.");
        }

        if (string.IsNullOrWhiteSpace(item.QueryResponse))
        {
            throw new InvalidOperationException(
                "Record a response before resolving the review query.");
        }

        var resolvedAt = DateTimeOffset.UtcNow;
        item.Status = YearEndReviewStatus.Reviewed;
        item.QueryResolvedAt = resolvedAt;
        item.QueryResolvedByUserId = userId;
        item.ReviewedAt = resolvedAt;
        item.ReviewedByUserId = userId;
        db.AuditEvents.Add(Audit(
            organisationId,
            userId,
            "YearEndReviewQueryResolved",
            periodId,
            new
            {
                Area = area,
                item.QueryAssignedToUserId,
                item.QueryDueDate,
                item.QueryRespondedAt,
                item.QueryRespondedByUserId
            }));
        await db.SaveChangesAsync(cancellationToken);
        return review;
    }

    public async Task<YearEndReview> ApproveAsync(
        string userId,
        Guid organisationId,
        Guid periodId,
        string approvalReference,
        CancellationToken cancellationToken = default)
    {
        await EnsureCanManageAsync(userId, organisationId);
        var review = await LoadForChangeAsync(organisationId, periodId, cancellationToken);
        EnsureOpen(review);

        var trimmedReference = approvalReference.Trim();
        if (string.IsNullOrWhiteSpace(trimmedReference))
        {
            throw new InvalidOperationException("Enter the final approval reference.");
        }

        if (trimmedReference.Length > 80)
        {
            throw new InvalidOperationException(
                "The final approval reference cannot exceed 80 characters.");
        }

        var incomplete = review.Items
            .Where(x => x.Status != YearEndReviewStatus.Reviewed)
            .Select(x => x.Area)
            .ToList();
        if (incomplete.Count > 0)
        {
            throw new InvalidOperationException(
                "Clear every year-end review item before recording final approval.");
        }

        review.ApprovedAt = DateTimeOffset.UtcNow;
        review.ApprovedByUserId = userId;
        review.ApprovalReference = trimmedReference;
        db.AuditEvents.Add(Audit(
            organisationId,
            userId,
            "YearEndReviewApproved",
            periodId,
            new { ApprovalReference = trimmedReference }));
        await db.SaveChangesAsync(cancellationToken);
        return review;
    }

    private async Task<YearEndReview> LoadForChangeAsync(
        Guid organisationId,
        Guid periodId,
        CancellationToken cancellationToken) =>
        await db.YearEndReviews
            .Include(x => x.AccountingPeriod)
            .Include(x => x.Items)
            .SingleOrDefaultAsync(
                x => x.OrganisationId == organisationId &&
                     x.AccountingPeriodId == periodId,
                cancellationToken)
        ?? throw new InvalidOperationException("Start the year-end review first.");

    private static void EnsureOpen(YearEndReview review)
    {
        if (review.AccountingPeriod.IsLocked)
        {
            throw new InvalidOperationException(
                "Unlock the accounting period before changing its year-end review.");
        }

        if (review.ApprovedAt is not null)
        {
            throw new InvalidOperationException(
                "The year-end review has final approval and cannot be changed.");
        }
    }

    private async Task EnsureCanManageAsync(string userId, Guid organisationId)
    {
        if (!await access.CanManageTeamAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException(
                "Only owners and administrators can manage the year-end review.");
        }
    }

    private async Task<bool> CanViewAsync(
        string userId,
        Guid organisationId,
        Guid periodId,
        CancellationToken cancellationToken) =>
        await access.CanManageTeamAsync(userId, organisationId) ||
        await db.YearEndReviewItems.AsNoTracking().AnyAsync(
            x => x.YearEndReview.OrganisationId == organisationId &&
                 x.YearEndReview.AccountingPeriodId == periodId &&
                 x.QueryAssignedToUserId == userId,
            cancellationToken);

    private async Task<bool> IsAssigneeCandidateAsync(
        string candidateUserId,
        Guid organisationId,
        CancellationToken cancellationToken)
    {
        if (await db.OrganisationMemberships.AsNoTracking().AnyAsync(
                x => x.OrganisationId == organisationId &&
                     x.UserId == candidateUserId,
                cancellationToken))
        {
            return true;
        }

        return await db.AccountantEngagements.AsNoTracking().AnyAsync(
            x => x.ClientOrganisationId == organisationId &&
                 x.RevokedAt == null &&
                 db.OrganisationMemberships.Any(m =>
                     m.OrganisationId == x.PracticeOrganisationId &&
                     m.UserId == candidateUserId),
            cancellationToken);
    }

    private static AuditEvent Audit(
        Guid organisationId,
        string userId,
        string eventType,
        Guid periodId,
        object evidence) =>
        new()
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = eventType,
            EntityType = nameof(AccountingPeriod),
            EntityId = periodId.ToString(),
            JsonData = JsonSerializer.Serialize(evidence)
        };
}
