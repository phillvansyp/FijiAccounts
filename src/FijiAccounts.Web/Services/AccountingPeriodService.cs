using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record AccountingPeriodRequest(Guid OrganisationId, string Name, DateOnly StartsOn, DateOnly EndsOn);

public sealed class AccountingPeriodService(ApplicationDbContext db, TenantAccessService access)
{
    public async Task<AccountingPeriod> CreateAsync(string userId, AccountingPeriodRequest request, CancellationToken ct = default)
    {
        if (!await access.CanManageTeamAsync(userId, request.OrganisationId)) throw new UnauthorizedAccessException("Only owners and administrators can manage accounting periods.");
        if (request.EndsOn < request.StartsOn) throw new InvalidOperationException("The period end cannot be before its start.");
        if (await db.AccountingPeriods.AnyAsync(x => x.OrganisationId == request.OrganisationId && request.StartsOn <= x.EndsOn && request.EndsOn >= x.StartsOn, ct)) throw new InvalidOperationException("Accounting periods cannot overlap.");
        var period = new AccountingPeriod { OrganisationId = request.OrganisationId, Name = request.Name.Trim(), StartsOn = request.StartsOn, EndsOn = request.EndsOn };
        db.AccountingPeriods.Add(period); db.AuditEvents.Add(Audit(request.OrganisationId, userId, "AccountingPeriodCreated", period, false)); await db.SaveChangesAsync(ct); return period;
    }

    public async Task SetLockedAsync(string userId, Guid organisationId, Guid periodId, bool locked, CancellationToken ct = default)
    {
        if (!await access.CanManageTeamAsync(userId, organisationId)) throw new UnauthorizedAccessException("Only owners and administrators can manage accounting periods.");
        var period = await db.AccountingPeriods.SingleOrDefaultAsync(x => x.Id == periodId && x.OrganisationId == organisationId, ct) ?? throw new InvalidOperationException("Accounting period not found.");
        period.IsLocked = locked; period.LockedAt = locked ? DateTimeOffset.UtcNow : null; period.LockedByUserId = locked ? userId : null;
        db.AuditEvents.Add(Audit(organisationId, userId, locked ? "AccountingPeriodLocked" : "AccountingPeriodUnlocked", period, locked)); await db.SaveChangesAsync(ct);
    }

    private static AuditEvent Audit(Guid organisationId, string userId, string eventType, AccountingPeriod period, bool locked) => new() { OrganisationId = organisationId, UserId = userId, EventType = eventType, EntityType = nameof(AccountingPeriod), EntityId = period.Id.ToString(), JsonData = JsonSerializer.Serialize(new { period.Name, period.StartsOn, period.EndsOn, Locked = locked }) };
}
