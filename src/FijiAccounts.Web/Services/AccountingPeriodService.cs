using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record AccountingPeriodRequest(
    Guid OrganisationId,
    string Name,
    DateOnly StartsOn,
    DateOnly EndsOn);

public sealed record AccountingPeriodUpdateRequest(
    Guid OrganisationId,
    Guid PeriodId,
    string Name,
    DateOnly StartsOn,
    DateOnly EndsOn);

public sealed record AccountingPeriodReadiness(
    int UnreconciledBankStatementLines,
    int IncompleteBankReconciliations,
    int DraftSalesInvoices,
    int DraftSupplierBills,
    int FixedAssetsRequiringDepreciation,
    int InventoryIntegrityWarnings)
{
    public bool IsReady =>
    UnreconciledBankStatementLines == 0 &&
    IncompleteBankReconciliations == 0 &&
    DraftSalesInvoices == 0 &&
    DraftSupplierBills == 0 &&
    FixedAssetsRequiringDepreciation == 0 &&
    InventoryIntegrityWarnings == 0;

    public int WarningCount =>
    UnreconciledBankStatementLines +
    IncompleteBankReconciliations +
    DraftSalesInvoices +
    DraftSupplierBills +
    FixedAssetsRequiringDepreciation +
    InventoryIntegrityWarnings;
}

public sealed class AccountingPeriodService(
    ApplicationDbContext db,
    TenantAccessService access)
{
    public async Task<AccountingPeriod> CreateAsync(
        string userId,
        AccountingPeriodRequest request,
        CancellationToken ct = default)
    {
        if (!await access.CanManageTeamAsync(
                userId,
                request.OrganisationId))
        {
            throw new UnauthorizedAccessException(
                "Only owners and administrators can manage accounting periods.");
        }

        if (request.EndsOn < request.StartsOn)
        {
            throw new InvalidOperationException(
                "The period end cannot be before its start.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
{
    throw new InvalidOperationException(
        "Enter an accounting period name.");
}

        var overlaps =
            await db.AccountingPeriods.AnyAsync(
                x =>
                    x.OrganisationId == request.OrganisationId &&
                    request.StartsOn <= x.EndsOn &&
                    request.EndsOn >= x.StartsOn,
                ct);

        if (overlaps)
        {
            throw new InvalidOperationException(
                "Accounting periods cannot overlap.");
        }

        var period =
            new AccountingPeriod
            {
                OrganisationId = request.OrganisationId,
                Name = request.Name.Trim(),
                StartsOn = request.StartsOn,
                EndsOn = request.EndsOn
            };

        db.AccountingPeriods.Add(period);

        db.AuditEvents.Add(
            Audit(
                request.OrganisationId,
                userId,
                "AccountingPeriodCreated",
                period,
                false));

        await db.SaveChangesAsync(ct);

        return period;
    }

    public async Task<AccountingPeriod> UpdateAsync(
    string userId,
    AccountingPeriodUpdateRequest request,
    CancellationToken ct = default)
{
    if (!await access.CanManageTeamAsync(
            userId,
            request.OrganisationId))
    {
        throw new UnauthorizedAccessException(
            "Only owners and administrators can manage accounting periods.");
    }

    if (request.EndsOn < request.StartsOn)
    {
        throw new InvalidOperationException(
            "The period end cannot be before its start.");
    }

    if (string.IsNullOrWhiteSpace(request.Name))
    {
        throw new InvalidOperationException(
            "Enter an accounting period name.");
    }

    var period =
        await db.AccountingPeriods
            .SingleOrDefaultAsync(
                x =>
                    x.Id == request.PeriodId &&
                    x.OrganisationId == request.OrganisationId,
                ct)
        ?? throw new InvalidOperationException(
            "Accounting period not found.");

    if (period.IsLocked)
    {
        throw new InvalidOperationException(
            "Unlock the accounting period before editing it.");
    }

    var overlaps =
        await db.AccountingPeriods.AnyAsync(
            x =>
                x.OrganisationId == request.OrganisationId &&
                x.Id != request.PeriodId &&
                request.StartsOn <= x.EndsOn &&
                request.EndsOn >= x.StartsOn,
            ct);

    if (overlaps)
    {
        throw new InvalidOperationException(
            "Accounting periods cannot overlap.");
    }

    period.Name = request.Name.Trim();
    period.StartsOn = request.StartsOn;
    period.EndsOn = request.EndsOn;

    db.AuditEvents.Add(
        Audit(
            request.OrganisationId,
            userId,
            "AccountingPeriodUpdated",
            period,
            false));

    await db.SaveChangesAsync(ct);

    return period;
}

    public async Task<AccountingPeriodReadiness> GetReadinessAsync(
        string userId,
        Guid organisationId,
        Guid periodId,
        CancellationToken ct = default)
    {
        if (!await access.CanManageTeamAsync(
                userId,
                organisationId))
        {
            throw new UnauthorizedAccessException(
                "Only owners and administrators can manage accounting periods.");
        }

        var period =
            await db.AccountingPeriods
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == periodId &&
                        x.OrganisationId == organisationId,
                    ct)
            ?? throw new InvalidOperationException(
                "Accounting period not found.");

        var unreconciledBankStatementLines =
            await db.BankStatementLines.CountAsync(
                x =>
                    x.OrganisationId == organisationId &&
                    x.TransactionDate >= period.StartsOn &&
                    x.TransactionDate <= period.EndsOn &&
                    x.ReconciledAt == null,
                ct);

        var incompleteBankReconciliations =
            await db.BankReconciliationSessions.CountAsync(
                x =>
                    x.OrganisationId == organisationId &&
                    !x.IsCompleted &&
                    x.StatementStartDate <= period.EndsOn &&
                    x.StatementEndDate >= period.StartsOn,
                ct);

        var draftSalesInvoices =
            await db.SalesInvoices.CountAsync(
                x =>
                    x.OrganisationId == organisationId &&
                    x.Status == InvoiceStatus.Draft &&
                    x.IssueDate >= period.StartsOn &&
                    x.IssueDate <= period.EndsOn,
                ct);

        var draftSupplierBills =
            await db.SupplierBillDrafts.CountAsync(
                x =>
                    x.OrganisationId == organisationId &&
                    x.BillDate >= period.StartsOn &&
                    x.BillDate <= period.EndsOn,
                ct);

        var fixedAssets =
    await db.FixedAssets
        .AsNoTracking()
        .Where(x =>
            x.OrganisationId == organisationId &&
            x.IsActive &&
            x.AcquisitionDate <= period.EndsOn)
        .Select(x => new
        {
            x.AcquisitionDate,
            x.Cost,
            x.ResidualValue,
            x.UsefulLifeMonths,
            PostedDepreciation =
                x.DepreciationEntries.Sum(d => (decimal?)d.Amount) ?? 0m
        })
        .ToListAsync(ct);

var fixedAssetsRequiringDepreciation =
    fixedAssets.Count(asset =>
    {
        var months =
            Math.Min(
                asset.UsefulLifeMonths,
                (period.EndsOn.Year - asset.AcquisitionDate.Year) * 12 +
                period.EndsOn.Month -
                asset.AcquisitionDate.Month +
                1);

        var targetDepreciation =
            Math.Round(
                (asset.Cost - asset.ResidualValue) *
                months /
                asset.UsefulLifeMonths,
                2);

        return targetDepreciation - asset.PostedDepreciation > 0;
    });

        var inventoryPositions =
    await db.InventoryMovements
        .AsNoTracking()
        .Where(x =>
            x.OrganisationId == organisationId &&
            x.MovementDate <= period.EndsOn)
        .GroupBy(x => x.ProductItemId)
        .Select(g => new
        {
            Quantity = g.Sum(x => x.QuantityChange),
            Value = g.Sum(x => x.ValueChange)
        })
        .ToListAsync(ct);

var inventoryIntegrityWarnings =
    inventoryPositions.Count(position =>
        position.Quantity < 0m ||
        position.Value < -0.01m ||
        (position.Quantity == 0m &&
         Math.Abs(position.Value) > 0.01m));

        return new AccountingPeriodReadiness(
    unreconciledBankStatementLines,
    incompleteBankReconciliations,
    draftSalesInvoices,
    draftSupplierBills,
    fixedAssetsRequiringDepreciation,
    inventoryIntegrityWarnings);
    }

    public async Task SetLockedAsync(
    string userId,
    Guid organisationId,
    Guid periodId,
    bool locked,
    bool acknowledgeWarnings = false,
    CancellationToken ct = default)
    {
        if (!await access.CanManageTeamAsync(
                userId,
                organisationId))
        {
            throw new UnauthorizedAccessException(
                "Only owners and administrators can manage accounting periods.");
        }

        var period =
            await db.AccountingPeriods
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == periodId &&
                        x.OrganisationId == organisationId,
                    ct)
            ?? throw new InvalidOperationException(
                "Accounting period not found.");

        AccountingPeriodReadiness? readiness = null;

if (locked)
{
    readiness =
        await GetReadinessAsync(
            userId,
            organisationId,
            periodId,
            ct);

    if (!readiness.IsReady && !acknowledgeWarnings)
    {
        throw new InvalidOperationException(
            "Review and acknowledge the outstanding period items before locking.");
    }
}

        period.IsLocked = locked;
        period.LockedAt =
            locked
                ? DateTimeOffset.UtcNow
                : null;
        period.LockedByUserId =
            locked
                ? userId
                : null;

        db.AuditEvents.Add(
    Audit(
        organisationId,
        userId,
        locked
            ? "AccountingPeriodLocked"
            : "AccountingPeriodUnlocked",
        period,
        locked,
        readiness,
        locked && !readiness!.IsReady && acknowledgeWarnings));

        await db.SaveChangesAsync(ct);
    }

    private static AuditEvent Audit(
    Guid organisationId,
    string userId,
    string eventType,
    AccountingPeriod period,
    bool locked,
    AccountingPeriodReadiness? readiness = null,
    bool warningsAcknowledged = false) =>
    new()
    {
        OrganisationId = organisationId,
        UserId = userId,
        EventType = eventType,
        EntityType = nameof(AccountingPeriod),
        EntityId = period.Id.ToString(),
        JsonData =
            JsonSerializer.Serialize(
                new
                {
                    period.Name,
                    period.StartsOn,
                    period.EndsOn,
                    Locked = locked,
                    UnreconciledBankStatementLines =
                        readiness?.UnreconciledBankStatementLines ?? 0,
                    IncompleteBankReconciliations =
                        readiness?.IncompleteBankReconciliations ?? 0,
                    DraftSalesInvoices =
                        readiness?.DraftSalesInvoices ?? 0,
                    DraftSupplierBills =
    readiness?.DraftSupplierBills ?? 0,
FixedAssetsRequiringDepreciation =
    readiness?.FixedAssetsRequiringDepreciation ?? 0,
    InventoryIntegrityWarnings =
    readiness?.InventoryIntegrityWarnings ?? 0,
WarningsAcknowledged =
    warningsAcknowledged
                })
    };
}