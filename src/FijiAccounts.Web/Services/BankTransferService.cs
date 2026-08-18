using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record BankTransferRequest(Guid OrganisationId, Guid FromAccountId, Guid ToAccountId, DateOnly Date, string Reference, string? Description, decimal Amount);
public sealed class BankTransferService(ApplicationDbContext db, TenantAccessService access, JournalPostingService posting)
{
    public async Task<BankTransfer> PostAsync(string userId, BankTransferRequest request, CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, request.OrganisationId)) throw new UnauthorizedAccessException("You cannot transfer funds for this organisation.");
        if (request.FromAccountId == request.ToAccountId) throw new InvalidOperationException("Choose two different bank accounts.");
        if (request.Amount <= 0 || string.IsNullOrWhiteSpace(request.Reference)) throw new InvalidOperationException("Enter a positive amount and reference.");
        var ids = new[] { request.FromAccountId, request.ToAccountId }; var accounts = await db.LedgerAccounts.Where(x => x.OrganisationId == request.OrganisationId && x.IsActive && x.IsBankAccount && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct); if (accounts.Count != 2) throw new InvalidOperationException("Both accounts must be active bank accounts in this organisation.");
        var reference = request.Reference.Trim(); if (await db.BankTransfers.AnyAsync(x => x.OrganisationId == request.OrganisationId && x.Reference == reference, ct)) throw new InvalidOperationException($"Transfer reference {reference} already exists.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct); var description = string.IsNullOrWhiteSpace(request.Description) ? $"Transfer from {accounts[request.FromAccountId].Name} to {accounts[request.ToAccountId].Name}" : request.Description.Trim();
        var journal = await posting.PostAsync(userId, new(request.OrganisationId, request.Date, reference, description, [new(request.ToAccountId, description, request.Amount, 0), new(request.FromAccountId, description, 0, request.Amount)]), ct);
        var transfer = new BankTransfer { OrganisationId = request.OrganisationId, FromBankAccountId = request.FromAccountId, ToBankAccountId = request.ToAccountId, TransferDate = request.Date, Reference = reference, Description = description, Amount = request.Amount, PostedJournalId = journal.Id, CreatedByUserId = userId }; db.BankTransfers.Add(transfer); db.AuditEvents.Add(new AuditEvent { OrganisationId = request.OrganisationId, UserId = userId, EventType = "BankTransferPosted", EntityType = nameof(BankTransfer), EntityId = transfer.Id.ToString(), JsonData = JsonSerializer.Serialize(new { transfer.Reference, transfer.Amount, transfer.FromBankAccountId, transfer.ToBankAccountId, JournalId = journal.Id }) }); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return transfer;
    }
}
