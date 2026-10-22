using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record BankLedgerInspectionRequest(
    Guid OrganisationId,
    string ExpectedLegalName,
    DateOnly StatementEndDate);

// Read-only operator diagnostic. It emits IDs and accounting evidence needed to
// prepare a reviewed, assertion-backed BankRepairPlan without exposing documents.
public sealed class BankLedgerInspectionService(ApplicationDbContext db)
{
    public async Task<string> RunAsync(
        BankLedgerInspectionRequest request,
        CancellationToken ct = default)
    {
        var organisation = await db.Organisations
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.OrganisationId, ct)
            ?? throw new InvalidOperationException("Organisation not found.");

        if (!string.Equals(
                organisation.LegalName,
                request.ExpectedLegalName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The organisation does not match the reviewed inspection request.");
        }

        var accounts = await db.LedgerAccounts
            .AsNoTracking()
            .Where(x =>
                x.OrganisationId == request.OrganisationId &&
                x.IsBankAccount)
            .OrderBy(x => x.Code)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Name,
                x.BankAccountNumber,
                x.IsActive
            })
            .ToListAsync(ct);

        var bankAccountIds = accounts.Select(x => x.Id).ToArray();
        var ledgerLines = await db.PostedJournalLines
            .AsNoTracking()
            .Where(x =>
                x.PostedJournal.OrganisationId == request.OrganisationId &&
                bankAccountIds.Contains(x.LedgerAccountId))
            .OrderBy(x => x.PostedJournal.EntryDate)
            .ThenBy(x => x.PostedJournal.SequenceNumber)
            .Select(x => new
            {
                x.Id,
                x.LedgerAccountId,
                x.PostedJournalId,
                x.PostedJournal.SequenceNumber,
                x.PostedJournal.EntryDate,
                x.PostedJournal.Reference,
                JournalDescription = x.PostedJournal.Description,
                x.Description,
                x.Debit,
                x.Credit
            })
            .ToListAsync(ct);

        var statements = await db.BankStatementLines
            .AsNoTracking()
            .Where(x => x.OrganisationId == request.OrganisationId)
            .OrderBy(x => x.TransactionDate)
            .Select(x => new
            {
                x.Id,
                x.BankAccountId,
                x.TransactionDate,
                x.Description,
                x.Reference,
                x.Amount,
                x.MatchedPostedJournalLineId,
                x.ReconciledAt,
                x.ImportBatchId,
                x.SourceHash
            })
            .ToListAsync(ct);

        var matchedLineIds = statements
            .Where(x => x.MatchedPostedJournalLineId.HasValue)
            .Select(x => x.MatchedPostedJournalLineId!.Value)
            .ToHashSet();

        var codedGroups = ledgerLines
            .Where(x =>
                x.Reference.StartsWith("BANK-", StringComparison.OrdinalIgnoreCase) &&
                (x.JournalDescription?.StartsWith(
                    "Coded from bank statement",
                    StringComparison.OrdinalIgnoreCase) ?? false))
            .GroupBy(x => new
            {
                x.LedgerAccountId,
                x.EntryDate,
                Reference = x.Reference.ToUpperInvariant(),
                Amount = x.Debit - x.Credit
            })
            .Where(x => x.Count() > 1)
            .Select(group => new
            {
                group.Key.LedgerAccountId,
                group.Key.EntryDate,
                group.Key.Reference,
                group.Key.Amount,
                Journals = group.Select(x => new
                {
                    x.PostedJournalId,
                    x.SequenceNumber,
                    BankLineId = x.Id,
                    IsMatched = matchedLineIds.Contains(x.Id),
                    x.Description
                }).ToArray()
            })
            .ToArray();

        var accountEvidence = accounts.Select(account =>
        {
            var lines = ledgerLines.Where(x => x.LedgerAccountId == account.Id).ToList();
            return new
            {
                account.Id,
                account.Code,
                account.Name,
                account.BankAccountNumber,
                account.IsActive,
                BalanceAtStatementEnd = lines
                    .Where(x => x.EntryDate <= request.StatementEndDate)
                    .Sum(x => x.Debit - x.Credit),
                CurrentBalance = lines.Sum(x => x.Debit - x.Credit),
                Entries = lines.Select(x => new
                {
                    x.PostedJournalId,
                    x.SequenceNumber,
                    x.EntryDate,
                    x.Reference,
                    x.JournalDescription,
                    x.Description,
                    Amount = x.Debit - x.Credit,
                    IsMatched = matchedLineIds.Contains(x.Id)
                }).ToArray()
            };
        }).ToArray();

        var result = new
        {
            organisation.Id,
            organisation.LegalName,
            WorkspaceId = OrganisationDisplay.WorkspaceId(organisation),
            request.StatementEndDate,
            Accounts = accountEvidence,
            DuplicateCodedTransactions = codedGroups,
            UnreconciledStatementTransactions = statements
                .Where(x => x.ReconciledAt is null)
                .Select(x => new
                {
                    x.Id,
                    x.BankAccountId,
                    x.TransactionDate,
                    x.Description,
                    x.Reference,
                    x.Amount,
                    x.ImportBatchId
                })
                .ToArray(),
            ReconciledStatementCount = statements.Count(x => x.ReconciledAt is not null),
            StatementCount = statements.Count
        };

        return JsonSerializer.Serialize(
            result,
            new JsonSerializerOptions { WriteIndented = true });
    }
}
