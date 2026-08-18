using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record BankRuleRequest(Guid OrganisationId, string Name, string DescriptionContains, BankRuleDirection Direction, Guid TargetAccountId);
public sealed class BankRuleService(ApplicationDbContext db, TenantAccessService access, JournalPostingService posting, BankReconciliationService reconciliation)
{
    public async Task<BankRule> CreateAsync(string userId, BankRuleRequest request, CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, request.OrganisationId)) throw new UnauthorizedAccessException("You cannot maintain bank rules for this organisation."); if (string.IsNullOrWhiteSpace(request.Name) || request.DescriptionContains.Trim().Length < 2) throw new InvalidOperationException("Enter a rule name and at least two matching characters.");
        if (!await db.LedgerAccounts.AnyAsync(x => x.Id == request.TargetAccountId && x.OrganisationId == request.OrganisationId && x.IsActive && !x.IsBankAccount, ct)) throw new InvalidOperationException("Choose an active non-bank target account.");
        var rule = new BankRule { OrganisationId = request.OrganisationId, Name = request.Name.Trim(), DescriptionContains = request.DescriptionContains.Trim(), Direction = request.Direction, TargetAccountId = request.TargetAccountId, CreatedByUserId = userId }; db.BankRules.Add(rule); db.AuditEvents.Add(Audit(request.OrganisationId, userId, "BankRuleCreated", rule.Id, new { rule.Name, rule.DescriptionContains, rule.Direction })); await db.SaveChangesAsync(ct); return rule;
    }
    public async Task ApplyAsync(string userId, Guid organisationId, Guid ruleId, Guid statementLineId, CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId)) throw new UnauthorizedAccessException("You cannot apply bank rules for this organisation."); var rule = await db.BankRules.SingleOrDefaultAsync(x => x.Id == ruleId && x.OrganisationId == organisationId && x.IsActive, ct) ?? throw new InvalidOperationException("Active bank rule not found."); var line = await db.BankStatementLines.SingleOrDefaultAsync(x => x.Id == statementLineId && x.OrganisationId == organisationId && x.ReconciledAt == null, ct) ?? throw new InvalidOperationException("Unreconciled statement line not found.");
        if (!Matches(rule, line)) throw new InvalidOperationException("The selected rule does not match this statement line."); await using var transaction = await db.Database.BeginTransactionAsync(ct); var amount = Math.Abs(line.Amount); var journalLines = line.Amount > 0 ? new[] { new JournalLineInput(line.BankAccountId, line.Description, amount, 0), new JournalLineInput(rule.TargetAccountId, line.Description, 0, amount) } : new[] { new JournalLineInput(rule.TargetAccountId, line.Description, amount, 0), new JournalLineInput(line.BankAccountId, line.Description, 0, amount) }; var journal = await posting.PostAsync(userId, new(organisationId, line.TransactionDate, line.Reference ?? $"BANK-{line.Id.ToString()[..8]}", $"Bank rule: {rule.Name} (tax out of scope)", journalLines), ct); var bankLine = journal.Lines.Single(x => x.LedgerAccountId == line.BankAccountId); await reconciliation.ReconcileAsync(userId, organisationId, line.Id, bankLine.Id, ct); db.AuditEvents.Add(Audit(organisationId, userId, "BankRuleApplied", rule.Id, new { rule.Name, StatementLineId = line.Id, line.Amount })); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }
    public static bool Matches(BankRule rule, BankStatementLine line) => line.Description.Contains(rule.DescriptionContains, StringComparison.OrdinalIgnoreCase) && (rule.Direction == BankRuleDirection.Any || rule.Direction == BankRuleDirection.MoneyIn && line.Amount > 0 || rule.Direction == BankRuleDirection.MoneyOut && line.Amount < 0);
    private static AuditEvent Audit(Guid organisationId, string userId, string eventType, Guid id, object data) => new() { OrganisationId = organisationId, UserId = userId, EventType = eventType, EntityType = nameof(BankRule), EntityId = id.ToString(), JsonData = JsonSerializer.Serialize(data) };
}
