using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record AccountingPeriodRequest(
    Guid OrganisationId,
    string Name,
    DateOnly StartsOn,
    DateOnly EndsOn);

public sealed record AccountingPeriodReadiness(
    int UnreconciledBankStatementLines,
    int IncompleteBankReconciliations,
    int DraftSalesInvoices,
    int DraftSupplierBills)
{
    public bool IsReady =>
        UnreconciledBankStatementLines == 0 &&
        IncompleteBankReconciliations == 0 &&
        DraftSalesInvoices == 0 &&
        DraftSupplierBills == 0;

    public int WarningCount =>
        UnreconciledBankStatementLines +
        IncompleteBankReconciliations +
        DraftSalesInvoices +
        DraftSupplierBills;
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

        return new AccountingPeriodReadiness(
            unreconciledBankStatementLines,
            incompleteBankReconciliations,
            draftSalesInvoices,
            draftSupplierBills);
    }

    public async Task SetLockedAsync(
        string userId,
        Guid organisationId,
        Guid periodId,
        bool locked,
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
                locked));

        await db.SaveChangesAsync(ct);
    }

    private static AuditEvent Audit(
        Guid organisationId,
        string userId,
        string eventType,
        AccountingPeriod period,
        bool locked) =>
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
                        Locked = locked
                    })
        };
}