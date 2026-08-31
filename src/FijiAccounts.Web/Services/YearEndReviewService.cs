using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

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
        await EnsureCanManageAsync(userId, organisationId);

        return await db.YearEndReviews.AsNoTracking()
            .Include(x => x.Items)
            .SingleOrDefaultAsync(
                x => x.OrganisationId == organisationId &&
                     x.AccountingPeriodId == periodId,
                cancellationToken);
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

        if (status == YearEndReviewStatus.QueryRaised &&
            string.IsNullOrWhiteSpace(trimmedNotes))
        {
            throw new InvalidOperationException("Enter the outstanding query in the review notes.");
        }

        var item = review.Items.Single(x => x.Area == area);
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
            new { Area = area, Status = status, Notes = trimmedNotes }));
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
