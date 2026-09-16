using System.Data;
using System.Text.Json;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record PayrollAccountTarget(string Role, string Code, string Name);
public sealed record PayrollLineMove(Guid LineId, Guid ExpectedAccountId, decimal Debit, decimal Credit, string Role);
public sealed record PayrollAccountSeparationPlan(string Id, Guid OrganisationId, string UserId,
    Guid ConnectionId, List<PayrollAccountTarget> Accounts, List<PayrollLineMove> Lines);

// Account setup and append-only reclassification of reviewed payroll lines. A preview
// exercises the same posting checks as apply, then rolls back the entire transaction.
public sealed class PayrollAccountSeparationService(ApplicationDbContext db,
    TenantAccessService access, JournalPostingService posting)
{
    private static readonly string[] Roles = ["Wages", "EmployerFnpf", "NetWages", "Paye", "Fnpf", "OtherDeductions"];

    public async Task<string> RunAsync(PayrollAccountSeparationPlan plan, bool apply, CancellationToken ct = default)
    {
        if (!await access.CanManageTeamAsync(plan.UserId, plan.OrganisationId) ||
            !await access.CanPostJournalsAsync(plan.UserId, plan.OrganisationId))
            throw new UnauthorizedAccessException("An authorised organisation administrator must configure payroll accounts.");
        if (string.IsNullOrWhiteSpace(plan.Id) || plan.Id.Length > 64 ||
            plan.Accounts.Count != Roles.Length || !Roles.All(r => plan.Accounts.Count(a => a.Role == r) == 1) ||
            plan.Accounts.Select(a => a.Code).Distinct().Count() != Roles.Length ||
            plan.Lines.Select(l => l.LineId).Distinct().Count() != plan.Lines.Count ||
            plan.Lines.Any(l => !Roles.Contains(l.Role)))
            throw new InvalidOperationException("Provide a unique operation ID, six distinct payroll accounts and unique source lines.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            if (await db.AuditEvents.AnyAsync(a => a.OrganisationId == plan.OrganisationId &&
                a.EventType == "PayrollAccountsSeparated" && a.EntityId == plan.Id, ct))
                return "Already applied; no changes made.";
            var connection = await db.PayrollIslandConnections.SingleAsync(c => c.Id == plan.ConnectionId &&
                c.OrganisationId == plan.OrganisationId && c.IsActive, ct);
            var oldMappings = new { connection.WagesExpenseAccountId, connection.EmployerContributionsExpenseAccountId,
                connection.NetWagesPayableAccountId, connection.PayePayableAccountId,
                connection.FnpfPayableAccountId, connection.OtherDeductionsPayableAccountId };
            var sourceIds = plan.Lines.Select(l => l.LineId).ToList();
            var sources = await db.PostedJournalLines.AsNoTracking().Include(l => l.PostedJournal)
                .Include(l => l.LedgerAccount).Where(l => sourceIds.Contains(l.Id) &&
                    l.PostedJournal.OrganisationId == plan.OrganisationId).ToDictionaryAsync(l => l.Id, ct);
            if (sources.Count != sourceIds.Count) throw new InvalidOperationException("A source payroll line was not found in this organisation.");
            var accounts = new Dictionary<string, LedgerAccount>();
            foreach (var target in plan.Accounts)
            {
                if (string.IsNullOrWhiteSpace(target.Code) || target.Code.Length > 20 ||
                    string.IsNullOrWhiteSpace(target.Name) || target.Name.Length > 160)
                    throw new InvalidOperationException("A valid account code and name are required.");
                var type = target.Role is "Wages" or "EmployerFnpf" ? AccountType.Expense : AccountType.Liability;
                var account = await db.LedgerAccounts.SingleOrDefaultAsync(a =>
                    a.OrganisationId == plan.OrganisationId && a.Code == target.Code, ct);
                if (account is null)
                {
                    account = new LedgerAccount { OrganisationId = plan.OrganisationId,
                        Code = target.Code, Name = target.Name, Type = type };
                    db.LedgerAccounts.Add(account);
                }
                if (!account.IsActive || account.IsBankAccount || account.Type != type || account.Name != target.Name)
                    throw new InvalidOperationException($"Account {target.Code} is already used for a different purpose.");
                accounts.Add(target.Role, account);
            }
            await db.SaveChangesAsync(ct);

            var created = new List<Guid>();
            foreach (var group in plan.Lines.GroupBy(l => sources[l.LineId].PostedJournalId))
            {
                var sourceJournal = sources[group.First().LineId].PostedJournal;
                var lines = new List<JournalLineInput>();
                foreach (var move in group)
                {
                    var source = sources[move.LineId];
                    var target = accounts[move.Role];
                    if (source.LedgerAccountId != move.ExpectedAccountId || source.Debit != move.Debit || source.Credit != move.Credit ||
                        source.LedgerAccount.IsBankAccount || source.LedgerAccount.Type != target.Type)
                        throw new InvalidOperationException("A source line changed or would move between accounting categories. No changes saved.");
                    if (source.LedgerAccountId == target.Id) continue;
                    if (await db.AuditEvents.AnyAsync(a => a.OrganisationId == plan.OrganisationId &&
                        a.EventType == "PayrollLineReclassified" && a.EntityId == source.Id.ToString(), ct))
                        throw new InvalidOperationException("A source line has already been reclassified.");
                    lines.Add(new(source.LedgerAccountId, source.Description, source.Credit, source.Debit,
                        source.BranchId, source.DivisionId, source.ProjectId, source.ProjectCostCodeId));
                    lines.Add(new(target.Id, source.Description, source.Debit, source.Credit,
                        source.BranchId, source.DivisionId, source.ProjectId, source.ProjectCostCodeId));
                }
                if (lines.Count == 0) continue;
                var journal = await posting.PostAsync(plan.UserId, new(plan.OrganisationId, sourceJournal.EntryDate,
                    $"PAY-SPLIT-{sourceJournal.Id:N}", $"Separate payroll accounts: {sourceJournal.Reference}",
                    lines, Purpose: JournalPurpose.Payroll, Currency: sourceJournal.Currency), ct);
                created.Add(journal.Id);
                foreach (var move in group.Where(m => sources[m.LineId].LedgerAccountId != accounts[m.Role].Id))
                    db.AuditEvents.Add(new AuditEvent { OrganisationId = plan.OrganisationId, UserId = plan.UserId,
                        EventType = "PayrollLineReclassified", EntityType = nameof(PostedJournalLine), EntityId = move.LineId.ToString(),
                        JsonData = JsonSerializer.Serialize(new { plan.Id, SourceJournalId = sourceJournal.Id,
                            CorrectionJournalId = journal.Id, move, TargetAccountId = accounts[move.Role].Id }) });
                await db.SaveChangesAsync(ct);
            }
            connection.WagesExpenseAccountId = accounts["Wages"].Id;
            connection.EmployerContributionsExpenseAccountId = accounts["EmployerFnpf"].Id;
            connection.NetWagesPayableAccountId = accounts["NetWages"].Id;
            connection.PayePayableAccountId = accounts["Paye"].Id;
            connection.FnpfPayableAccountId = accounts["Fnpf"].Id;
            connection.OtherDeductionsPayableAccountId = accounts["OtherDeductions"].Id;
            connection.UpdatedByUserId = plan.UserId;
            connection.UpdatedAt = DateTimeOffset.UtcNow;
            db.AuditEvents.Add(new AuditEvent { OrganisationId = plan.OrganisationId, UserId = plan.UserId,
                EventType = "PayrollAccountsSeparated", EntityType = nameof(PayrollIslandConnection), EntityId = plan.Id,
                JsonData = JsonSerializer.Serialize(new { Plan = plan, PreviousMappings = oldMappings,
                    Accounts = accounts.ToDictionary(a => a.Key, a => a.Value.Id), Journals = created }) });
            await db.SaveChangesAsync(ct);
            if (apply) await transaction.CommitAsync(ct);
            else await transaction.RollbackAsync(ct);
            return $"{(apply ? "Applied" : "Preview passed; no changes saved")}: six payroll mappings, {created.Count} balanced reclassification journals. Bank entries unchanged.";
        }
        finally { db.ChangeTracker.Clear(); }
    }
}
