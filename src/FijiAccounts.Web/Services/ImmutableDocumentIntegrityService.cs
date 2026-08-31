using System.Security.Cryptography;
using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed class ImmutableDocumentIntegrityService(
    ApplicationDbContext db,
    TenantAccessService access,
    OrganisationUpdateBroker updates)
{
    private const string SystemUserId = "system:immutable-document-integrity";
    private const string NotificationEntityType = "ImmutableDocumentIntegrity";

    public async Task<ImmutableDocumentIntegrityScan?> GetLatestAsync(
        string userId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        if (await access.FindAsync(userId, organisationId) is null)
        {
            return null;
        }

        return await db.ImmutableDocumentIntegrityScans
            .AsNoTracking()
            .Where(x => x.OrganisationId == organisationId)
            .OrderByDescending(x => x.CompletedAtTicks)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ImmutableDocumentIntegrityScan> ScanAsync(
        string userId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        if (!await access.CanManageTeamAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException(
                "Only an organisation owner or administrator can run an integrity scan.");
        }

        return await ScanCoreAsync(userId, organisationId, cancellationToken);
    }

    public Task<ImmutableDocumentIntegrityScan> ScanSystemAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default) =>
        ScanCoreAsync(SystemUserId, organisationId, cancellationToken);

    private async Task<ImmutableDocumentIntegrityScan> ScanCoreAsync(
        string userId,
        Guid organisationId,
        CancellationToken cancellationToken)
    {
        if (!await db.Organisations.AsNoTracking().AnyAsync(
                x => x.Id == organisationId,
                cancellationToken))
        {
            throw new InvalidOperationException("Organisation not found.");
        }

        var objectCount = 0;
        var verifiedObjectCount = 0;
        var integrityFailureCount = 0;
        await foreach (var item in db.ImmutableDocumentObjects
                           .AsNoTracking()
                           .Where(x => x.OrganisationId == organisationId)
                           .OrderBy(x => x.Id)
                           .Select(x => new
                           {
                               x.Content,
                               x.ContentLength,
                               x.Sha256
                           })
                           .AsAsyncEnumerable()
                           .WithCancellation(cancellationToken))
        {
            objectCount++;
            var hash = Convert.ToHexString(SHA256.HashData(item.Content));
            if (item.Content.LongLength == item.ContentLength &&
                hash.Equals(item.Sha256, StringComparison.Ordinal))
            {
                verifiedObjectCount++;
            }
            else
            {
                integrityFailureCount++;
            }
        }

        var linkedDocumentCount =
            await db.BusinessPartyDocuments.AsNoTracking().CountAsync(
                x => x.OrganisationId == organisationId && x.ImmutableDocumentObjectId != null,
                cancellationToken) +
            await db.SupplierBillAttachments.AsNoTracking().CountAsync(
                x => x.OrganisationId == organisationId && x.ImmutableDocumentObjectId != null,
                cancellationToken) +
            await db.BankStatementImportDocuments.AsNoTracking().CountAsync(
                x => x.OrganisationId == organisationId && x.ImmutableDocumentObjectId != null,
                cancellationToken) +
            await db.YearEndHandoverPackSnapshots.AsNoTracking().CountAsync(
                x => x.OrganisationId == organisationId,
                cancellationToken) +
            await db.YearEndReviewAttachments.AsNoTracking().CountAsync(
                x => x.OrganisationId == organisationId,
                cancellationToken);
        var legacyDocumentCount =
            await db.BusinessPartyDocuments.AsNoTracking().CountAsync(
                x => x.OrganisationId == organisationId && x.ImmutableDocumentObjectId == null,
                cancellationToken) +
            await db.SupplierBillAttachments.AsNoTracking().CountAsync(
                x => x.OrganisationId == organisationId && x.ImmutableDocumentObjectId == null,
                cancellationToken) +
            await db.BankStatementImportDocuments.AsNoTracking().CountAsync(
                x => x.OrganisationId == organisationId && x.ImmutableDocumentObjectId == null,
                cancellationToken);

        var missingObjectReferenceCount =
            await db.BusinessPartyDocuments.AsNoTracking().CountAsync(
                x => x.OrganisationId == organisationId &&
                     x.ImmutableDocumentObjectId != null &&
                     !db.ImmutableDocumentObjects.Any(o =>
                         o.Id == x.ImmutableDocumentObjectId &&
                         o.OrganisationId == organisationId),
                cancellationToken) +
            await db.SupplierBillAttachments.AsNoTracking().CountAsync(
                x => x.OrganisationId == organisationId &&
                     x.ImmutableDocumentObjectId != null &&
                     !db.ImmutableDocumentObjects.Any(o =>
                         o.Id == x.ImmutableDocumentObjectId &&
                         o.OrganisationId == organisationId),
                cancellationToken) +
            await db.BankStatementImportDocuments.AsNoTracking().CountAsync(
                x => x.OrganisationId == organisationId &&
                     x.ImmutableDocumentObjectId != null &&
                     !db.ImmutableDocumentObjects.Any(o =>
                         o.Id == x.ImmutableDocumentObjectId &&
                         o.OrganisationId == organisationId),
                cancellationToken) +
            await db.YearEndHandoverPackSnapshots.AsNoTracking().CountAsync(
                x => x.OrganisationId == organisationId &&
                     !db.ImmutableDocumentObjects.Any(o =>
                         o.Id == x.ImmutableDocumentObjectId &&
                         o.OrganisationId == organisationId),
                cancellationToken) +
            await db.YearEndReviewAttachments.AsNoTracking().CountAsync(
                x => x.OrganisationId == organisationId &&
                     !db.ImmutableDocumentObjects.Any(o =>
                         o.Id == x.ImmutableDocumentObjectId &&
                         o.OrganisationId == organisationId),
                cancellationToken);

        var unreferencedObjectCount = await db.ImmutableDocumentObjects
            .AsNoTracking()
            .CountAsync(
                x => x.OrganisationId == organisationId &&
                     !db.BusinessPartyDocuments.Any(d => d.ImmutableDocumentObjectId == x.Id) &&
                     !db.SupplierBillAttachments.Any(d => d.ImmutableDocumentObjectId == x.Id) &&
                     !db.BankStatementImportDocuments.Any(d => d.ImmutableDocumentObjectId == x.Id) &&
                     !db.YearEndHandoverPackSnapshots.Any(d => d.ImmutableDocumentObjectId == x.Id) &&
                     !db.YearEndReviewAttachments.Any(d => d.ImmutableDocumentObjectId == x.Id),
                cancellationToken);

        var status = integrityFailureCount == 0 &&
                     missingObjectReferenceCount == 0 &&
                     legacyDocumentCount == 0
            ? ImmutableDocumentIntegrityStatus.Healthy
            : ImmutableDocumentIntegrityStatus.AttentionRequired;
        var completedAt = DateTimeOffset.UtcNow;
        var scan = new ImmutableDocumentIntegrityScan
        {
            OrganisationId = organisationId,
            ObjectCount = objectCount,
            LinkedDocumentCount = linkedDocumentCount,
            VerifiedObjectCount = verifiedObjectCount,
            IntegrityFailureCount = integrityFailureCount,
            MissingObjectReferenceCount = missingObjectReferenceCount,
            LegacyDocumentCount = legacyDocumentCount,
            UnreferencedObjectCount = unreferencedObjectCount,
            Status = status,
            CompletedAt = completedAt,
            CompletedAtTicks = completedAt.UtcTicks,
            CompletedByUserId = userId
        };
        db.ImmutableDocumentIntegrityScans.Add(scan);
        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = "ImmutableDocumentIntegrityScanned",
            EntityType = nameof(ImmutableDocumentIntegrityScan),
            EntityId = scan.Id.ToString(),
            JsonData = JsonSerializer.Serialize(new
            {
                Status = scan.Status.ToString(),
                scan.ObjectCount,
                scan.LinkedDocumentCount,
                scan.VerifiedObjectCount,
                scan.IntegrityFailureCount,
                scan.MissingObjectReferenceCount,
                scan.LegacyDocumentCount,
                scan.UnreferencedObjectCount
            })
        });

        await UpdateNotificationAsync(scan, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        updates.Publish(organisationId);
        return scan;
    }

    private async Task UpdateNotificationAsync(
        ImmutableDocumentIntegrityScan scan,
        CancellationToken cancellationToken)
    {
        var openNotifications = await db.Notifications
            .Where(x =>
                x.OrganisationId == scan.OrganisationId &&
                x.RelatedEntityType == NotificationEntityType &&
                x.Status != NotificationStatus.Resolved)
            .ToListAsync(cancellationToken);
        if (scan.Status == ImmutableDocumentIntegrityStatus.AttentionRequired)
        {
            if (openNotifications.Count == 0)
            {
                var createdAt = DateTimeOffset.UtcNow;
                db.Notifications.Add(new Notification
                {
                    OrganisationId = scan.OrganisationId,
                    Title = "Retained document integrity needs attention",
                    Message = $"Integrity failures: {scan.IntegrityFailureCount}; missing references: {scan.MissingObjectReferenceCount}; legacy files: {scan.LegacyDocumentCount}.",
                    Type = NotificationType.System,
                    Severity = NotificationSeverity.Critical,
                    RelatedEntityType = NotificationEntityType,
                    RelatedEntityId = scan.Id.ToString(),
                    CreatedAt = createdAt,
                    CreatedAtTicks = createdAt.UtcTicks
                });
            }
            return;
        }

        var resolvedAt = DateTimeOffset.UtcNow;
        foreach (var notification in openNotifications)
        {
            notification.Status = NotificationStatus.Resolved;
            notification.ResolvedAt = resolvedAt;
            notification.IsRead = true;
            notification.ReadAt = resolvedAt;
        }
    }
}
