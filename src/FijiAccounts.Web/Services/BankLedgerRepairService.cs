using System.Data;
using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record BankRepairBalance(DateOnly Date, decimal Before, decimal After);
public sealed record BankRepairJournal(string Key, Guid SourceJournalId, bool Reverse, DateOnly Date, string Reason);
public sealed record BankRepairMatch(Guid StatementId, Guid ExpectedLineId, Guid? TargetLineId, string? ReplacementKey);
public sealed record BankRepairAdjustment(string Key, DateOnly Date, string OffsetAccountCode, decimal Amount, string Reason);
public sealed record BankRepairPlan(string RepairId, Guid OrganisationId, Guid BankAccountId, string UserId,
    List<BankRepairBalance> Balances, List<BankRepairJournal> Journals, List<BankRepairMatch> Matches,
    List<BankRepairAdjustment>? Adjustments = null);

// Operator-only maintenance: explicit reviewed plan, normal posting permissions and locks,
// append-only corrections, all-or-nothing balance assertions and idempotent retries.
public sealed class BankLedgerRepairService(ApplicationDbContext db, TenantAccessService access,
    JournalPostingService posting, BankReconciliationService reconciliation,
    BankReconciliationSessionService sessions)
{
    public async Task<string> RunAsync(BankRepairPlan plan, bool apply, CancellationToken ct = default)
    {
        var actingUserId = plan.UserId;
        if (string.IsNullOrWhiteSpace(actingUserId))
        {
            var owners = await db.OrganisationMemberships
                .AsNoTracking()
                .Where(x =>
                    x.OrganisationId == plan.OrganisationId &&
                    x.Role == OrganisationRole.Owner)
                .Select(x => new { x.UserId, x.CreatedAt })
                .ToListAsync(ct);
            actingUserId = owners
                .OrderBy(x => x.CreatedAt)
                .Select(x => x.UserId)
                .FirstOrDefault()
                ?? throw new InvalidOperationException(
                    "The organisation has no owner to attribute the repair to.");
        }

        if (!await access.CanManageTeamAsync(actingUserId, plan.OrganisationId) ||
            !await access.CanPostJournalsAsync(actingUserId, plan.OrganisationId))
            throw new UnauthorizedAccessException("An authorised organisation administrator must run the repair.");
        var adjustments = plan.Adjustments ?? [];
        var allKeys = plan.Journals.Select(x => x.Key)
            .Concat(adjustments.Select(x => x.Key))
            .ToArray();
        if (string.IsNullOrWhiteSpace(plan.RepairId) || plan.RepairId.Length > 64 ||
            plan.Balances.Count == 0 || allKeys.Distinct().Count() != allKeys.Length)
            throw new InvalidOperationException("A unique repair ID, journal keys and balance checks are required.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (await db.AuditEvents.AnyAsync(x => x.OrganisationId == plan.OrganisationId &&
            x.EventType == "BankLedgerRepairCompleted" && x.EntityId == plan.RepairId, ct))
            return "Already applied; no changes made.";
        if (!await db.LedgerAccounts.AnyAsync(x => x.Id == plan.BankAccountId &&
            x.OrganisationId == plan.OrganisationId && x.IsBankAccount, ct))
            throw new InvalidOperationException("Bank account not found in this organisation.");
        foreach (var balance in plan.Balances)
            await AssertBalance(balance.Date, balance.Before);
        var created = new Dictionary<string, PostedJournal>();
        foreach (var correction in plan.Journals)
        {
            var source = await db.PostedJournals.AsNoTracking().Include(x => x.Lines)
                .SingleAsync(x => x.Id == correction.SourceJournalId && x.OrganisationId == plan.OrganisationId, ct);
            if (!source.Lines.Any(x => x.LedgerAccountId == plan.BankAccountId) || string.IsNullOrWhiteSpace(correction.Reason))
                throw new InvalidOperationException("Every correction must identify this bank and explain its purpose.");
            var result = await posting.PostAsync(actingUserId, new JournalPostRequest(plan.OrganisationId,
                correction.Date, $"FIX-{correction.Key}", correction.Reason,
                source.Lines.Select(x => new JournalLineInput(x.LedgerAccountId, x.Description,
                    correction.Reverse ? x.Credit : x.Debit, correction.Reverse ? x.Debit : x.Credit,
                    x.BranchId, x.DivisionId, x.ProjectId, x.ProjectCostCodeId)).ToList(), Currency: source.Currency), ct);
            created.Add(correction.Key, result);
            db.AuditEvents.Add(new AuditEvent { OrganisationId = plan.OrganisationId, UserId = actingUserId,
                EventType = "BankLedgerRepairJournal", EntityType = nameof(PostedJournal), EntityId = result.Id.ToString(),
                JsonData = JsonSerializer.Serialize(new { plan.RepairId, correction.SourceJournalId,
                    OriginalJournalId = correction.Reverse ? (Guid?)source.Id : null,
                    ReversalJournalId = correction.Reverse ? (Guid?)result.Id : null,
                    ReplacementJournalId = correction.Reverse ? null : (Guid?)result.Id, correction.Reason }) });
            await db.SaveChangesAsync(ct);
        }
        foreach (var adjustment in adjustments)
        {
            if (adjustment.Amount == 0 || string.IsNullOrWhiteSpace(adjustment.Reason))
                throw new InvalidOperationException("Every balance adjustment must have a non-zero amount and a reason.");
            var offset = await db.LedgerAccounts.SingleOrDefaultAsync(x =>
                x.OrganisationId == plan.OrganisationId &&
                x.Code == adjustment.OffsetAccountCode &&
                x.IsActive &&
                !x.IsBankAccount &&
                x.Type == FijiAccounts.Domain.Accounting.AccountType.Equity,
                ct) ?? throw new InvalidOperationException(
                    $"Active equity offset account {adjustment.OffsetAccountCode} was not found.");
            var amount = Math.Abs(adjustment.Amount);
            var lines = adjustment.Amount > 0
                ? new List<JournalLineInput>
                {
                    new(plan.BankAccountId, adjustment.Reason, amount, 0),
                    new(offset.Id, adjustment.Reason, 0, amount)
                }
                : new List<JournalLineInput>
                {
                    new(offset.Id, adjustment.Reason, amount, 0),
                    new(plan.BankAccountId, adjustment.Reason, 0, amount)
                };
            var result = await posting.PostAsync(actingUserId, new JournalPostRequest(
                plan.OrganisationId,
                adjustment.Date,
                $"FIX-{adjustment.Key}",
                adjustment.Reason,
                lines), ct);
            created.Add(adjustment.Key, result);
            db.AuditEvents.Add(new AuditEvent
            {
                OrganisationId = plan.OrganisationId,
                UserId = actingUserId,
                EventType = "BankLedgerRepairAdjustment",
                EntityType = nameof(PostedJournal),
                EntityId = result.Id.ToString(),
                JsonData = JsonSerializer.Serialize(new
                {
                    plan.RepairId,
                    adjustment.Key,
                    adjustment.Date,
                    adjustment.OffsetAccountCode,
                    adjustment.Amount,
                    adjustment.Reason
                })
            });
            await db.SaveChangesAsync(ct);
        }
        foreach (var match in plan.Matches)
        {
            var statement = await db.BankStatementLines.AsNoTracking().SingleAsync(x =>
                x.Id == match.StatementId && x.OrganisationId == plan.OrganisationId && x.BankAccountId == plan.BankAccountId, ct);
            if (statement.MatchedPostedJournalLineId != match.ExpectedLineId)
                throw new InvalidOperationException("A statement match changed since the repair was prepared.");
            var target = match.TargetLineId ?? created[match.ReplacementKey!].Lines
                .Single(x => x.LedgerAccountId == plan.BankAccountId).Id;
            await reconciliation.UnreconcileAsync(actingUserId, plan.OrganisationId, statement.Id,
                $"Repair {plan.RepairId}: replace invalid or duplicate bank coding with the supported entry.", ct);
            await reconciliation.ReconcileAsync(actingUserId, plan.OrganisationId, statement.Id, target, ct);
        }
        foreach (var balance in plan.Balances)
            await AssertBalance(balance.Date, balance.After);
        var sessionIds = await db.BankReconciliationSessions.AsNoTracking().Where(x =>
            x.OrganisationId == plan.OrganisationId && x.BankAccountId == plan.BankAccountId && !x.IsCompleted)
            .Select(x => x.Id).ToListAsync(ct);
        foreach (var id in sessionIds) await sessions.RefreshAsync(actingUserId, plan.OrganisationId, id, ct);
        db.AuditEvents.Add(new AuditEvent { OrganisationId = plan.OrganisationId, UserId = actingUserId,
            EventType = "BankLedgerRepairCompleted", EntityType = "BankLedgerRepair", EntityId = plan.RepairId,
            JsonData = JsonSerializer.Serialize(plan) });
        await db.SaveChangesAsync(ct);
        if (apply) await transaction.CommitAsync(ct);
        else await transaction.RollbackAsync(ct);
        db.ChangeTracker.Clear();
        return apply ? "Repair applied and balances verified." : "Preview passed; transaction rolled back. No changes saved.";

        async Task AssertBalance(DateOnly date, decimal expected)
        {
            var lines = await db.PostedJournalLines.AsNoTracking().Where(x =>
                x.PostedJournal.OrganisationId == plan.OrganisationId && x.LedgerAccountId == plan.BankAccountId &&
                x.PostedJournal.EntryDate <= date).Select(x => new { x.Debit, x.Credit }).ToListAsync(ct);
            var actual = lines.Sum(x => x.Debit - x.Credit);
            if (actual != expected)
                throw new InvalidOperationException($"Balance check failed at {date}: expected {expected}, found {actual}. Entire repair rolled back.");
        }
    }
}
