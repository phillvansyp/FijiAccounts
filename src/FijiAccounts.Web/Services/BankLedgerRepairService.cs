using System.Data;
using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record BankRepairBalance(DateOnly Date, decimal Before, decimal After);
public sealed record BankRepairJournal(string Key, Guid SourceJournalId, bool Reverse, DateOnly Date, string Reason);
public sealed record BankRepairMatch(Guid StatementId, Guid ExpectedLineId, Guid? TargetLineId, string? ReplacementKey);
public sealed record BankRepairPlan(string RepairId, Guid OrganisationId, Guid BankAccountId, string UserId,
    List<BankRepairBalance> Balances, List<BankRepairJournal> Journals, List<BankRepairMatch> Matches);

// Operator-only maintenance: explicit reviewed plan, normal posting permissions and locks,
// append-only corrections, all-or-nothing balance assertions and idempotent retries.
public sealed class BankLedgerRepairService(ApplicationDbContext db, TenantAccessService access,
    JournalPostingService posting, BankReconciliationService reconciliation,
    BankReconciliationSessionService sessions)
{
    public async Task<string> RunAsync(BankRepairPlan plan, bool apply, CancellationToken ct = default)
    {
        if (!await access.CanManageTeamAsync(plan.UserId, plan.OrganisationId) ||
            !await access.CanPostJournalsAsync(plan.UserId, plan.OrganisationId))
            throw new UnauthorizedAccessException("An authorised organisation administrator must run the repair.");
        if (string.IsNullOrWhiteSpace(plan.RepairId) || plan.RepairId.Length > 64 ||
            plan.Balances.Count == 0 || plan.Journals.Select(x => x.Key).Distinct().Count() != plan.Journals.Count)
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
            var result = await posting.PostAsync(plan.UserId, new JournalPostRequest(plan.OrganisationId,
                correction.Date, $"FIX-{correction.Key}", correction.Reason,
                source.Lines.Select(x => new JournalLineInput(x.LedgerAccountId, x.Description,
                    correction.Reverse ? x.Credit : x.Debit, correction.Reverse ? x.Debit : x.Credit,
                    x.BranchId, x.DivisionId, x.ProjectId, x.ProjectCostCodeId)).ToList(), Currency: source.Currency), ct);
            created.Add(correction.Key, result);
            db.AuditEvents.Add(new AuditEvent { OrganisationId = plan.OrganisationId, UserId = plan.UserId,
                EventType = "BankLedgerRepairJournal", EntityType = nameof(PostedJournal), EntityId = result.Id.ToString(),
                JsonData = JsonSerializer.Serialize(new { plan.RepairId, correction.SourceJournalId,
                    OriginalJournalId = correction.Reverse ? (Guid?)source.Id : null,
                    ReversalJournalId = correction.Reverse ? (Guid?)result.Id : null,
                    ReplacementJournalId = correction.Reverse ? null : (Guid?)result.Id, correction.Reason }) });
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
            await reconciliation.UnreconcileAsync(plan.UserId, plan.OrganisationId, statement.Id,
                $"Repair {plan.RepairId}: replace invalid or duplicate bank coding with the supported entry.", ct);
            await reconciliation.ReconcileAsync(plan.UserId, plan.OrganisationId, statement.Id, target, ct);
        }
        foreach (var balance in plan.Balances)
            await AssertBalance(balance.Date, balance.After);
        var sessionIds = await db.BankReconciliationSessions.AsNoTracking().Where(x =>
            x.OrganisationId == plan.OrganisationId && x.BankAccountId == plan.BankAccountId && !x.IsCompleted)
            .Select(x => x.Id).ToListAsync(ct);
        foreach (var id in sessionIds) await sessions.RefreshAsync(plan.UserId, plan.OrganisationId, id, ct);
        db.AuditEvents.Add(new AuditEvent { OrganisationId = plan.OrganisationId, UserId = plan.UserId,
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
