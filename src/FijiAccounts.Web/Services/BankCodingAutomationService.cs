using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record BankCodingAutomationResult(int Applied, int NeedsReview)
{
    public static BankCodingAutomationResult Empty { get; } = new(0, 0);

    public BankCodingAutomationResult Add(BankCodingAutomationResult other) =>
        new(Applied + other.Applied, NeedsReview + other.NeedsReview);
}

public sealed class BankCodingAutomationService(
    ApplicationDbContext db,
    TenantAccessService access,
    BankTransactionCodingService transactionCoding,
    BankReconciliationSessionService reconciliationSessions)
{
    public async Task<BankCodingAutomationResult> ApplyAllAsync(
        string userId,
        Guid organisationId,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId))
            throw new UnauthorizedAccessException("You cannot apply remembered bank coding for this organisation.");

        var memoryIds = await db.BankCodingMemories
            .AsNoTracking()
            .Where(x => x.OrganisationId == organisationId)
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.Id)
            .ToListAsync(ct);

        var result = BankCodingAutomationResult.Empty;
        foreach (var memoryId in memoryIds)
            result = result.Add(await ApplyAsync(userId, organisationId, memoryId, ct));

        return result;
    }

    public async Task<BankCodingAutomationResult> ApplyAsync(
        string userId,
        Guid organisationId,
        Guid memoryId,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId))
            throw new UnauthorizedAccessException("You cannot apply remembered bank coding for this organisation.");

        var memory = await db.BankCodingMemories
            .AsNoTracking()
            .Include(x => x.TargetAccount)
            .SingleOrDefaultAsync(x =>
                x.Id == memoryId &&
                x.OrganisationId == organisationId,
                ct)
            ?? throw new InvalidOperationException("Remembered bank coding was not found.");

        if (!memory.TargetAccount.IsActive || memory.TargetAccount.IsBankAccount)
            return BankCodingAutomationResult.Empty;

        var divisionIsAvailable = await db.Divisions
            .AsNoTracking()
            .AnyAsync(x =>
                x.Id == memory.DivisionId &&
                x.IsActive &&
                x.Branch.OrganisationId == organisationId,
                ct);
        if (!divisionIsAvailable)
            return BankCodingAutomationResult.Empty;

        var candidates = await db.BankStatementLines
            .AsNoTracking()
            .Where(x =>
                x.OrganisationId == organisationId &&
                x.ReconciledAt == null &&
                x.Amount == memory.Amount)
            .OrderBy(x => x.TransactionDate)
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.BankAccountId,
                x.TransactionDate,
                x.Description,
                x.Reference,
                x.Amount
            })
            .ToListAsync(ct);

        var applied = 0;
        var needsReview = 0;
        foreach (var candidate in candidates.Where(x =>
                     BankCodingMemoryService.NormalizePayee(x.Description) == memory.PayeeKey))
        {
            if (await RequiresDocumentReviewAsync(
                    organisationId,
                    candidate.TransactionDate,
                    candidate.Description,
                    candidate.Reference,
                    candidate.Amount,
                    ct))
            {
                needsReview++;
                continue;
            }

            try
            {
                var journal = await transactionCoding.PostAndReconcileAsync(
                    userId,
                    new(
                        organisationId,
                        candidate.Id,
                        memory.TargetAccount.Code,
                        memory.Description,
                        memory.VatTreatment,
                        DivisionId: memory.DivisionId),
                    ct);

                db.AuditEvents.Add(new AuditEvent
                {
                    OrganisationId = organisationId,
                    UserId = userId,
                    EventType = "RememberedBankCodingApplied",
                    EntityType = nameof(BankStatementLine),
                    EntityId = candidate.Id.ToString(),
                    JsonData = JsonSerializer.Serialize(new
                    {
                        BankCodingMemoryId = memory.Id,
                        PostedJournalId = journal.Id,
                        memory.PayeeKey,
                        memory.Amount,
                        memory.TargetAccountId,
                        memory.DivisionId,
                        memory.VatTreatment
                    })
                });
                await db.SaveChangesAsync(ct);

                var affectedSessionIds = await db.BankReconciliationSessions
                    .AsNoTracking()
                    .Where(x =>
                        x.OrganisationId == organisationId &&
                        !x.IsCompleted &&
                        x.BankAccountId == candidate.BankAccountId &&
                        candidate.TransactionDate >= x.StatementStartDate &&
                        candidate.TransactionDate <= x.StatementEndDate)
                    .Select(x => x.Id)
                    .ToListAsync(ct);
                foreach (var sessionId in affectedSessionIds)
                    await reconciliationSessions.RefreshAsync(userId, organisationId, sessionId, ct);

                applied++;
            }
            catch (InvalidOperationException)
            {
                // Existing ledger matches, completed reconciliations and locked
                // periods need a person to choose the correct treatment.
                needsReview++;
            }
        }

        return new(applied, needsReview);
    }

    private async Task<bool> RequiresDocumentReviewAsync(
        Guid organisationId,
        DateOnly transactionDate,
        string description,
        string? reference,
        decimal amount,
        CancellationToken ct)
    {
        var statementText = NormalizeMatchText($"{description} {reference}");
        var absoluteAmount = Math.Abs(Math.Round(amount, 2, MidpointRounding.AwayFromZero));

        if (amount < 0)
        {
            var bills = await db.SupplierBills
                .AsNoTracking()
                .Include(x => x.Supplier)
                .Where(x =>
                    x.OrganisationId == organisationId &&
                    (x.Status == BillStatus.Posted || x.Status == BillStatus.PartPaid) &&
                    x.Total > x.AmountPaid + x.AmountCredited)
                .ToListAsync(ct);
            return bills.Any(x =>
                Math.Abs(Math.Round(x.Total - x.AmountPaid - x.AmountCredited, 2, MidpointRounding.AwayFromZero) - absoluteAmount) <= 0.01m &&
                x.BillDate <= transactionDate.AddDays(3) &&
                (BankDocumentReferenceMatcher.Matches(x.SupplierReference, description, reference) ||
                 BankDocumentReferenceMatcher.Matches(x.BillNumber, description, reference) ||
                 ContainsPartyName(statementText, x.Supplier.Name)));
        }

        var invoices = await db.SalesInvoices
            .AsNoTracking()
            .Include(x => x.Customer)
            .Where(x =>
                x.OrganisationId == organisationId &&
                (x.Status == InvoiceStatus.Posted || x.Status == InvoiceStatus.PartPaid) &&
                x.Total > x.AmountPaid + x.AmountCredited)
            .ToListAsync(ct);
        return invoices.Any(x =>
            Math.Abs(Math.Round(x.Total - x.AmountPaid - x.AmountCredited, 2, MidpointRounding.AwayFromZero) - absoluteAmount) <= 0.01m &&
            x.IssueDate <= transactionDate.AddDays(3) &&
            (BankDocumentReferenceMatcher.Matches(x.InvoiceNumber, description, reference) ||
             ContainsPartyName(statementText, x.Customer.Name)));
    }

    private static bool ContainsPartyName(string statementText, string? partyName)
    {
        var normalizedParty = NormalizeMatchText(partyName);
        return normalizedParty.Length > 2 &&
               statementText.Contains(normalizedParty, StringComparison.Ordinal);
    }

    private static string NormalizeMatchText(string? value) =>
        new((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
}
