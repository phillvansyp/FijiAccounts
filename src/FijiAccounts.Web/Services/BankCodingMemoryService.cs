using System.Text.Json;
using FijiAccounts.Web.Data;
using FijiAccounts.Domain.Tax;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record RememberBankCodingRequest(
    Guid OrganisationId,
    string Payee,
    decimal Amount,
    Guid TargetAccountId,
    Guid DivisionId,
    string Description,
    VatTreatment VatTreatment);

public sealed class BankCodingMemoryService(
    ApplicationDbContext db,
    TenantAccessService access)
{
    public async Task<BankCodingMemory?> FindAsync(
        string userId,
        Guid organisationId,
        string payee,
        decimal amount,
        CancellationToken ct = default)
    {
        if (await access.FindAsync(userId, organisationId) is null)
            throw new UnauthorizedAccessException("You cannot view bank coding for this organisation.");

        var key = NormalizePayee(payee);
        return await db.BankCodingMemories
            .AsNoTracking()
            .Include(x => x.TargetAccount)
            .SingleOrDefaultAsync(x =>
                x.OrganisationId == organisationId &&
                x.PayeeKey == key &&
                x.Amount == amount,
                ct);
    }

    public async Task<BankCodingMemory> RememberAsync(
        string userId,
        RememberBankCodingRequest request,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, request.OrganisationId))
            throw new UnauthorizedAccessException("You cannot remember bank coding for this organisation.");

        var accountExists = await db.LedgerAccounts.AnyAsync(x =>
            x.Id == request.TargetAccountId &&
            x.OrganisationId == request.OrganisationId &&
            x.IsActive &&
            !x.IsBankAccount,
            ct);
        if (!accountExists)
            throw new InvalidOperationException("Choose an active non-bank account before remembering this coding.");

        var divisionExists = await db.Divisions.AnyAsync(x =>
            x.Id == request.DivisionId &&
            x.Branch.OrganisationId == request.OrganisationId &&
            x.IsActive,
            ct);
        if (!divisionExists)
            throw new InvalidOperationException("Choose an active branch and division before remembering this coding.");

        var key = NormalizePayee(request.Payee);
        var memory = await db.BankCodingMemories.SingleOrDefaultAsync(x =>
            x.OrganisationId == request.OrganisationId &&
            x.PayeeKey == key &&
            x.Amount == request.Amount,
            ct);

        var now = DateTimeOffset.UtcNow;
        if (memory is null)
        {
            memory = new BankCodingMemory
            {
                OrganisationId = request.OrganisationId,
                PayeeKey = key,
                Amount = request.Amount,
                TargetAccountId = request.TargetAccountId,
                DivisionId = request.DivisionId,
                Description = request.Description.Trim(),
                VatTreatment = request.VatTreatment,
                CreatedAt = now,
                UpdatedAt = now,
                UpdatedByUserId = userId
            };
            db.BankCodingMemories.Add(memory);
        }
        else
        {
            memory.TargetAccountId = request.TargetAccountId;
            memory.DivisionId = request.DivisionId;
            memory.Description = request.Description.Trim();
            memory.VatTreatment = request.VatTreatment;
            memory.UpdatedAt = now;
            memory.UpdatedByUserId = userId;
        }

        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = request.OrganisationId,
            UserId = userId,
            EventType = "BankCodingRemembered",
            EntityType = nameof(BankCodingMemory),
            EntityId = memory.Id.ToString(),
            JsonData = JsonSerializer.Serialize(new
            {
                request.Payee,
                request.Amount,
                request.TargetAccountId,
                request.DivisionId,
                request.VatTreatment
            })
        });

        await db.SaveChangesAsync(ct);
        return memory;
    }

    public static string NormalizePayee(string payee) =>
        string.Join(' ', payee
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToUpperInvariant();
}
