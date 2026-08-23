using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record PlatformOverview(
    int TenantCount,
    int ActiveTenantCount,
    int SuspendedTenantCount,
    int DemoTenantCount,
    int CompanyCount,
    int UserCount,
    int MembershipCount,
    int PostedInvoiceCount,
    int SupplierBillCount,
    int PostedJournalCount,
    IReadOnlyList<PlatformTenantRow> Tenants);

public sealed record PlatformTenantRow(
    Guid Id,
    string Name,
    bool IsDemo,
    TenantStatus Status,
    string PresentationCurrency,
    int CompanyCount,
    int UserCount,
    string Jurisdictions,
    int TransactionCount,
    DateTimeOffset? LastActivity,
    DateTimeOffset CreatedAt);

public sealed record PlatformCompanyRow(
    Guid Id,
    string LegalName,
    string? TradingName,
    string CountryCode,
    string BaseCurrency,
    int BranchCount,
    int DepartmentCount,
    int MemberCount,
    int CustomerCount,
    int SupplierCount,
    int SalesInvoiceCount,
    int SupplierBillCount,
    int JournalCount,
    DateTimeOffset? LastActivity);

public sealed record PlatformTenantDetails(
    Guid Id,
    string Name,
    bool IsDemo,
    TenantStatus Status,
    string PresentationCurrency,
    string? InternalNotes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SuspendedAt,
    int DistinctUserCount,
    IReadOnlyList<PlatformCompanyRow> Companies,
    IReadOnlyList<PlatformAuditEvent> RecentPlatformEvents);

public sealed class PlatformAdministrationService(
    ApplicationDbContext db,
    PlatformAdminAccessService access)
{
    public async Task<PlatformOverview> GetOverviewAsync(
        string userId,
        CancellationToken ct = default)
    {
        await RequireAdminAsync(userId, ct);
        var groups = await db.OrganisationGroups.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        var companies = await db.Organisations.AsNoTracking().Where(x => x.OrganisationGroupId != null).ToListAsync(ct);
        var companyIds = companies.Select(x => x.Id).ToArray();
        var memberships = await db.OrganisationMemberships.AsNoTracking()
            .Where(x => companyIds.Contains(x.OrganisationId)).ToListAsync(ct);
        var invoiceCounts = await db.SalesInvoices.AsNoTracking()
            .Where(x => companyIds.Contains(x.OrganisationId))
            .GroupBy(x => x.OrganisationId).Select(x => new { Id = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var billCounts = await db.SupplierBills.AsNoTracking()
            .Where(x => companyIds.Contains(x.OrganisationId))
            .GroupBy(x => x.OrganisationId).Select(x => new { Id = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var journalRows = await db.PostedJournals.AsNoTracking()
            .Where(x => companyIds.Contains(x.OrganisationId))
            .Select(x => new { x.OrganisationId, x.PostedAt })
            .ToListAsync(ct);
        var journalStats = journalRows
            .GroupBy(x => x.OrganisationId)
            .ToDictionary(x => x.Key, x => new { Count = x.Count(), Last = (DateTimeOffset?)x.Max(y => y.PostedAt) });

        var rows = groups.Select(group =>
        {
            var tenantCompanies = companies.Where(x => x.OrganisationGroupId == group.Id).ToArray();
            var tenantCompanyIds = tenantCompanies.Select(x => x.Id).ToHashSet();
            var invoices = tenantCompanies.Sum(x => invoiceCounts.GetValueOrDefault(x.Id));
            var bills = tenantCompanies.Sum(x => billCounts.GetValueOrDefault(x.Id));
            var journals = tenantCompanies.Sum(x => journalStats.GetValueOrDefault(x.Id)?.Count ?? 0);
            return new PlatformTenantRow(
                group.Id, group.Name, group.IsDemo, group.Status, group.PresentationCurrency,
                tenantCompanies.Length,
                memberships.Where(x => tenantCompanyIds.Contains(x.OrganisationId)).Select(x => x.UserId).Distinct().Count(),
                string.Join(", ", tenantCompanies.Select(x => x.CountryCode).Distinct().Order()),
                invoices + bills + journals,
                tenantCompanies.Select(x => journalStats.GetValueOrDefault(x.Id)?.Last).Where(x => x is not null).DefaultIfEmpty().Max(),
                group.CreatedAt);
        }).ToList();

        return new PlatformOverview(
            groups.Count,
            groups.Count(x => x.Status == TenantStatus.Active),
            groups.Count(x => x.Status == TenantStatus.Suspended),
            groups.Count(x => x.IsDemo),
            companies.Count,
            await db.Users.CountAsync(ct),
            memberships.Count,
            invoiceCounts.Values.Sum(),
            billCounts.Values.Sum(),
            journalStats.Values.Sum(x => x.Count),
            rows);
    }

    public async Task<PlatformTenantDetails?> GetTenantAsync(
        string userId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        await RequireAdminAsync(userId, ct);
        var group = await db.OrganisationGroups.AsNoTracking().SingleOrDefaultAsync(x => x.Id == tenantId, ct);
        if (group is null) return null;
        var companies = await db.Organisations.AsNoTracking().Where(x => x.OrganisationGroupId == tenantId).OrderBy(x => x.LegalName).ToListAsync(ct);
        var rows = new List<PlatformCompanyRow>();
        foreach (var company in companies)
        {
            var partyCounts = await db.BusinessParties.AsNoTracking()
                .Where(x => x.OrganisationId == company.Id)
                .GroupBy(_ => 1)
                .Select(x => new
                {
                    Customers = x.Count(y => (y.Type & PartyType.Customer) != 0),
                    Suppliers = x.Count(y => (y.Type & PartyType.Supplier) != 0)
                }).SingleOrDefaultAsync(ct);
            var companyJournalDates = await db.PostedJournals
                .Where(x => x.OrganisationId == company.Id)
                .Select(x => x.PostedAt)
                .ToListAsync(ct);
            rows.Add(new PlatformCompanyRow(
                company.Id, company.LegalName, company.TradingName, company.CountryCode, company.BaseCurrency,
                await db.Branches.CountAsync(x => x.OrganisationId == company.Id, ct),
                await db.OrganisationUnits.CountAsync(x => x.OrganisationId == company.Id && x.Type == OrganisationUnitType.Department, ct),
                await db.OrganisationMemberships.CountAsync(x => x.OrganisationId == company.Id, ct),
                partyCounts?.Customers ?? 0, partyCounts?.Suppliers ?? 0,
                await db.SalesInvoices.CountAsync(x => x.OrganisationId == company.Id, ct),
                await db.SupplierBills.CountAsync(x => x.OrganisationId == company.Id, ct),
                companyJournalDates.Count,
                companyJournalDates.Count == 0 ? null : companyJournalDates.Max()));
        }

        var companyIds = companies.Select(x => x.Id).ToArray();
        var recentPlatformEvents = await db.PlatformAuditEvents
            .AsNoTracking()
            .Where(x => x.OrganisationGroupId == tenantId)
            .ToListAsync(ct);
        return new PlatformTenantDetails(
            group.Id, group.Name, group.IsDemo, group.Status, group.PresentationCurrency,
            group.InternalNotes, group.CreatedAt, group.SuspendedAt,
            await db.OrganisationMemberships.Where(x => companyIds.Contains(x.OrganisationId)).Select(x => x.UserId).Distinct().CountAsync(ct),
            rows,
            recentPlatformEvents
                .OrderByDescending(x => x.OccurredAt)
                .Take(20)
                .ToList());
    }

    public async Task SetTenantStatusAsync(
        string userId,
        Guid tenantId,
        TenantStatus status,
        string reason,
        CancellationToken ct = default)
    {
        await RequireAdminAsync(userId, ct);
        if (!Enum.IsDefined(status)) throw new InvalidOperationException("Select a valid tenant status.");
        if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("Enter a reason for changing tenant status.");
        if (reason.Trim().Length > 500) throw new InvalidOperationException("The status reason cannot exceed 500 characters.");
        var group = await db.OrganisationGroups.SingleOrDefaultAsync(x => x.Id == tenantId, ct)
            ?? throw new InvalidOperationException("Tenant not found.");
        var previous = group.Status;
        if (previous == status) return;
        group.Status = status;
        group.SuspendedAt = status == TenantStatus.Suspended ? DateTimeOffset.UtcNow : null;
        db.PlatformAuditEvents.Add(new PlatformAuditEvent
        {
            AdministratorUserId = userId,
            EventType = "TenantStatusChanged",
            OrganisationGroupId = tenantId,
            Reason = reason.Trim(),
            JsonData = JsonSerializer.Serialize(new { PreviousStatus = previous.ToString(), NewStatus = status.ToString() })
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task RequireAdminAsync(string userId, CancellationToken ct)
    {
        if (!await access.IsPlatformAdministratorAsync(userId, ct))
        {
            throw new UnauthorizedAccessException("Platform administrator access is required.");
        }
    }
}
