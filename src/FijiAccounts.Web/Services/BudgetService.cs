using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record BudgetRequest(Guid OrganisationId, Guid AccountId, DateOnly Month, decimal Amount);
public sealed class BudgetService(ApplicationDbContext db, TenantAccessService access)
{
    public async Task<AccountBudget> SetAsync(string userId, BudgetRequest request, CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, request.OrganisationId)) throw new UnauthorizedAccessException("You cannot maintain budgets for this organisation.");
        if (request.Amount < 0) throw new InvalidOperationException("A budget amount cannot be negative.");
        var month = new DateOnly(request.Month.Year, request.Month.Month, 1);
        if (!await db.LedgerAccounts.AnyAsync(x => x.Id == request.AccountId && x.OrganisationId == request.OrganisationId && x.IsActive && (x.Type == AccountType.Revenue || x.Type == AccountType.Expense), ct)) throw new InvalidOperationException("Select an active revenue or expense account.");
        var budget = await db.AccountBudgets.SingleOrDefaultAsync(x => x.OrganisationId == request.OrganisationId && x.LedgerAccountId == request.AccountId && x.Month == month, ct);
        var eventType = budget is null ? "BudgetCreated" : "BudgetUpdated";
        if (budget is null) { budget = new AccountBudget { OrganisationId = request.OrganisationId, LedgerAccountId = request.AccountId, Month = month, Amount = request.Amount, UpdatedByUserId = userId }; db.AccountBudgets.Add(budget); }
        else { budget.Amount = request.Amount; budget.UpdatedAt = DateTimeOffset.UtcNow; budget.UpdatedByUserId = userId; }
        db.AuditEvents.Add(new AuditEvent { OrganisationId = request.OrganisationId, UserId = userId, EventType = eventType, EntityType = nameof(AccountBudget), EntityId = budget.Id.ToString(), JsonData = JsonSerializer.Serialize(new { request.AccountId, Month = month, request.Amount }) }); await db.SaveChangesAsync(ct); return budget;
    }
}
