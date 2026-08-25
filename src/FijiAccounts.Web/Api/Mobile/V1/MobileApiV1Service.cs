using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Api.Mobile.V1;

public sealed class MobileApiV1Service(
    ApplicationDbContext db,
    TenantAccessService access,
    NotificationService notifications,
    MobileIdempotencyService idempotency)
{
    public async Task<IReadOnlyList<MobileOrganisationSummary>> ListOrganisationsAsync(
        string userId)
    {
        var organisations = await access.ListAsync(userId);

        return organisations
            .Select(item => new MobileOrganisationSummary(
                item.Organisation.Id,
                item.Organisation.LegalName,
                item.Organisation.TradingName,
                item.Organisation.CountryCode,
                item.Organisation.BaseCurrency,
                item.Organisation.Kind.ToString(),
                item.AccessLabel,
                item.IsClient))
            .ToList();
    }

    public async Task<MobileOrganisationCapabilities?> GetCapabilitiesAsync(
        string userId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var organisation = await access.FindAsync(userId, organisationId);
        if (organisation is null)
        {
            return null;
        }

        var canPostJournals = await access.CanPostJournalsAsync(userId, organisationId);
        var canManageContacts = await access.CanManageContactsAsync(userId, organisationId);
        var canManageTeam = await access.CanManageTeamAsync(userId, organisationId);
        var branches = await access.ListAccessibleBranchesAsync(
            userId,
            organisationId,
            cancellationToken);

        return new MobileOrganisationCapabilities(
            organisationId,
            organisation.AccessLabel,
            organisation.IsClient,
            CanRead: true,
            canPostJournals,
            canManageContacts,
            canManageTeam,
            branches.Select(branch => new MobileBranchAccess(
                    branch.Id,
                    branch.Code,
                    branch.Name,
                    branch.IsDefault,
                    branch.Divisions
                        .Select(division => new MobileDivisionAccess(
                            division.Id,
                            division.Code,
                            division.Name,
                            division.IsDefault))
                        .ToList()))
                .ToList());
    }

    public async Task<MobileDashboardResponse?> GetDashboardAsync(
        string userId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var organisation = await access.FindAsync(userId, organisationId);
        if (organisation is null)
        {
            return null;
        }

        var divisionScope = await access.GetReportDivisionScopeAsync(
            userId,
            organisationId,
            cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var cashPosition = await db.PostedJournalLines
            .AsNoTracking()
            .Where(line =>
                line.PostedJournal.OrganisationId == organisationId &&
                line.LedgerAccount.IsBankAccount &&
                (divisionScope == null ||
                 line.DivisionId.HasValue && divisionScope.Contains(line.DivisionId.Value)))
            .SumAsync(line => (decimal?)(line.Debit - line.Credit), cancellationToken) ?? 0;

        var salesInvoices = db.SalesInvoices
            .AsNoTracking()
            .Where(invoice =>
                invoice.OrganisationId == organisationId &&
                invoice.Status != InvoiceStatus.Draft &&
                invoice.Status != InvoiceStatus.Voided &&
                invoice.AmountPaid + invoice.AmountCredited < invoice.Total &&
                (divisionScope == null ||
                 invoice.DivisionId.HasValue && divisionScope.Contains(invoice.DivisionId.Value)));
        var supplierBills = db.SupplierBills
            .AsNoTracking()
            .Where(bill =>
                bill.OrganisationId == organisationId &&
                bill.Status != BillStatus.Voided &&
                bill.AmountPaid + bill.AmountCredited < bill.Total &&
                (divisionScope == null ||
                 bill.DivisionId.HasValue && divisionScope.Contains(bill.DivisionId.Value)));

        return new MobileDashboardResponse(
            organisationId,
            today,
            organisation.Organisation.BaseCurrency,
            cashPosition,
            await salesInvoices.SumAsync(
                invoice => (decimal?)(invoice.Total - invoice.AmountPaid - invoice.AmountCredited),
                cancellationToken) ?? 0,
            await supplierBills.SumAsync(
                bill => (decimal?)(bill.Total - bill.AmountPaid - bill.AmountCredited),
                cancellationToken) ?? 0,
            await salesInvoices.CountAsync(invoice => invoice.DueDate < today, cancellationToken),
            await supplierBills.CountAsync(bill => bill.DueDate < today, cancellationToken),
            await notifications.GetUnreadCountAsync(userId, organisationId, cancellationToken));
    }

    public async Task<MobilePage<MobileNotificationSummary>?> ListNotificationsAsync(
        string userId,
        Guid organisationId,
        long? beforeCreatedAtTicks,
        Guid beforeId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (await access.FindAsync(userId, organisationId) is null)
        {
            return null;
        }

        var items = await notifications.GetUnreadPageAsync(
            userId,
            organisationId,
            beforeCreatedAtTicks,
            beforeId,
            limit + 1,
            cancellationToken);
        var hasMore = items.Count > limit;
        var pageItems = items.Take(limit).ToList();
        var summaries = pageItems.Select(item => new MobileNotificationSummary(
                item.Id,
                item.Title,
                item.Message,
                item.Type.ToString(),
                item.Severity.ToString(),
                item.Status.ToString(),
                item.RelatedEntityType,
                item.RelatedEntityId,
                item.Amount,
                item.Currency,
                item.CreatedAt))
            .ToList();
        var last = hasMore ? pageItems[^1] : null;
        return new MobilePage<MobileNotificationSummary>(
            summaries,
            last is null ? null : MobileApiCursor.Encode(last.CreatedAtTicks, last.Id));
    }

    public async Task<MobileIdempotentCommandResult?> MarkNotificationReadAsync(
        string userId,
        Guid organisationId,
        Guid notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (await access.FindAsync(userId, organisationId) is null)
        {
            return null;
        }

        var operation = $"mobile-v1:notification:{notificationId}:read";
        return await idempotency.ExecuteAsync(
            userId,
            organisationId,
            idempotencyKey,
            operation,
            operation,
            async () => await notifications.MarkReadAsync(
                userId,
                organisationId,
                notificationId,
                cancellationToken)
                ? new MobileStoredCommandResult(StatusCodes.Status204NoContent)
                : new MobileStoredCommandResult(
                    StatusCodes.Status404NotFound,
                    "notification_not_found"),
            cancellationToken);
    }
}
