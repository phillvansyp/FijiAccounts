using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record BankTransactionCodingRequest(
    Guid OrganisationId,
    Guid StatementLineId,
    string TargetAccountCode,
    string Description,
    VatTreatment VatTreatment,
    Guid? TransferToBankAccountId = null);

public sealed class BankTransactionCodingService(
    ApplicationDbContext db,
    TenantAccessService access,
    JournalPostingService posting,
    BankReconciliationService reconciliation)
{
    public async Task ReopenCodingAsync(
        string userId,
        Guid organisationId,
        Guid statementLineId,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot change bank coding for this organisation.");
        }

        var statement = await db.BankStatementLines
            .SingleOrDefaultAsync(
                x => x.Id == statementLineId &&
                     x.OrganisationId == organisationId,
                ct)
            ?? throw new InvalidOperationException(
                "Statement line not found.");

        if (statement.ReconciledAt is null ||
            statement.MatchedPostedJournalLineId is null)
        {
            throw new InvalidOperationException(
                "This statement line is not reconciled.");
        }

    var completedReconciliationExists =
    await reconciliation.IsInsideCompletedReconciliationAsync(
        organisationId,
        statement.BankAccountId,
        statement.TransactionDate,
        ct);

if (completedReconciliationExists)
{
    throw new InvalidOperationException(
        "Bank coding inside a completed reconciliation period cannot be reopened.");
}

        var matched = await db.PostedJournalLines
            .AsNoTracking()
            .SingleAsync(
                x => x.Id == statement.MatchedPostedJournalLineId,
                ct);

        var original = await db.PostedJournals
            .AsNoTracking()
            .Include(x => x.Lines)
            .SingleAsync(
                x => x.Id == matched.PostedJournalId &&
                     x.OrganisationId == organisationId,
                ct);

        if (!(original.Description?.StartsWith(
                "Coded from bank statement",
                StringComparison.OrdinalIgnoreCase) ?? false))
        {
            throw new InvalidOperationException(
                "Only transactions created by bank coding can be changed here.");
        }

        await using var transaction =
            await db.Database.BeginTransactionAsync(ct);

        var reversal = await posting.PostAsync(
            userId,
            new(
                organisationId,
                statement.TransactionDate,
                $"REV-{original.Reference}",
                $"Reverse bank coding for correction: {statement.Description}",
                original.Lines
                    .Select(x => new JournalLineInput(
                        x.LedgerAccountId,
                        $"Reverse {x.Description}",
                        x.Credit,
                        x.Debit))
                    .ToList()),
            ct);

        var linkedTransfer = await db.BankTransfers
            .SingleOrDefaultAsync(
                x => x.OrganisationId == organisationId &&
                     x.PostedJournalId == original.Id,
                ct);

        if (linkedTransfer is not null)
        {
            db.BankTransfers.Remove(linkedTransfer);
        }

        statement.MatchedPostedJournalLineId = null;
        statement.ReconciledAt = null;
        statement.ReconciledByUserId = null;

        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = "BankTransactionCodingReopened",
            EntityType = nameof(BankStatementLine),
            EntityId = statement.Id.ToString(),
            JsonData = JsonSerializer.Serialize(new
            {
                OriginalJournalId = original.Id,
                ReversalJournalId = reversal.Id
            })
        });

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<PostedJournal> PostAndReconcileAsync(
        string userId,
        BankTransactionCodingRequest request,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(
                userId,
                request.OrganisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot code bank transactions for this organisation.");
        }

        var statement = await db.BankStatementLines
            .SingleOrDefaultAsync(
                x => x.Id == request.StatementLineId &&
                     x.OrganisationId == request.OrganisationId,
                ct)
            ?? throw new InvalidOperationException(
                "Statement line not found.");

        if (statement.ReconciledAt is not null)
        {
            throw new InvalidOperationException(
                "This statement line has already been reconciled.");
        }

        if (statement.Amount == 0)
        {
            throw new InvalidOperationException(
                "A zero-value statement line cannot be posted.");
        }

        var description =
            string.IsNullOrWhiteSpace(request.Description)
                ? statement.Description
                : request.Description.Trim();

        var amount = Math.Abs(statement.Amount);

        var statementAccount = await db.LedgerAccounts
    .SingleOrDefaultAsync(
        x => x.Id == statement.BankAccountId &&
             x.OrganisationId == request.OrganisationId &&
             x.IsActive &&
             x.IsBankAccount,
        ct)
    ?? throw new InvalidOperationException(
        "The statement bank/card account is not available.");

var isCreditCard =
    statementAccount.BankAccountKind == BankAccountKind.CreditCard;

        /*
         * INTERNAL BANK TRANSFER
         */
        if (request.TransferToBankAccountId is not null)
        {
            return await PostOrMatchInternalTransferAsync(
                userId,
                request,
                statement,
                description,
                amount,
                ct);
        }

        /*
         * NORMAL BANK CODING
         */
        var targetCode = request.TargetAccountCode?.Trim() ?? "";

        var target = await db.LedgerAccounts
            .SingleOrDefaultAsync(
                x => x.Code == targetCode &&
                     x.OrganisationId == request.OrganisationId &&
                     x.IsActive &&
                     !x.IsBankAccount,
                ct)
            ?? throw new InvalidOperationException(
                "Choose an active non-bank account.");

        var tax = new FijiVatSchedule()
            .CalculateFromInclusive(
                new Money(amount, "FJD"),
                statement.TransactionDate,
                request.VatTreatment);

        var lines = new List<JournalLineInput>();

if (!isCreditCard)
{
    /*
     * BANK / DEBIT-EFTPOS ACCOUNT
     *
     * Positive statement amount:
     *     Dr Bank
     *     Cr Income / other account
     *
     * Negative statement amount:
     *     Dr Expense / other account
     *     Cr Bank
     */
    if (statement.Amount > 0)
    {
        lines.Add(new JournalLineInput(
            statement.BankAccountId,
            description,
            amount,
            0));

        lines.Add(new JournalLineInput(
            target.Id,
            description,
            0,
            tax.Exclusive.Amount));

        if (tax.Vat.Amount > 0)
        {
            lines.Add(new JournalLineInput(
                await ControlAccountId(
                    request.OrganisationId,
                    "2100",
                    "VAT Payable",
                    ct),
                description,
                0,
                tax.Vat.Amount));
        }
    }
    else
    {
        lines.Add(new JournalLineInput(
            target.Id,
            description,
            tax.Exclusive.Amount,
            0));

        if (tax.Vat.Amount > 0)
        {
            lines.Add(new JournalLineInput(
                await ControlAccountId(
                    request.OrganisationId,
                    "1150",
                    "VAT Receivable",
                    ct),
                description,
                tax.Vat.Amount,
                0));
        }

        lines.Add(new JournalLineInput(
            statement.BankAccountId,
            description,
            0,
            amount));
    }
}
else
{
    /*
     * CREDIT CARD LIABILITY
     *
     * Our statement convention remains:
     *
     * Negative = purchase / charge
     * Positive = payment / refund
     *
     * A purchase increases the credit-card liability:
     *
     *     Dr Expense
     *     Dr VAT Receivable
     *     Cr Credit Card
     *
     * A refund / other credit reduces the liability:
     *
     *     Dr Credit Card
     *     Cr Income / other account
     *     Cr VAT Payable
     */

    if (statement.Amount < 0)
    {
        lines.Add(new JournalLineInput(
            target.Id,
            description,
            tax.Exclusive.Amount,
            0));

        if (tax.Vat.Amount > 0)
        {
            lines.Add(new JournalLineInput(
                await ControlAccountId(
                    request.OrganisationId,
                    "1150",
                    "VAT Receivable",
                    ct),
                description,
                tax.Vat.Amount,
                0));
        }

        lines.Add(new JournalLineInput(
            statement.BankAccountId,
            description,
            0,
            amount));
    }
    else
{
    lines.Add(new JournalLineInput(
        statement.BankAccountId,
        description,
        amount,
        0));

    lines.Add(new JournalLineInput(
        target.Id,
        description,
        0,
        tax.Exclusive.Amount));

    if (tax.Vat.Amount > 0)
    {
        var vatAccountCode =
            target.Type == AccountType.Expense
                ? "1150"
                : "2100";

        var vatAccountName =
            target.Type == AccountType.Expense
                ? "VAT Receivable"
                : "VAT Payable";

        lines.Add(new JournalLineInput(
            await ControlAccountId(
                request.OrganisationId,
                vatAccountCode,
                vatAccountName,
                ct),
            description,
            0,
            tax.Vat.Amount));
    }
}
}

        await using var transaction =
            await db.Database.BeginTransactionAsync(ct);

        var journal = await posting.PostAsync(
            userId,
            new(
                request.OrganisationId,
                statement.TransactionDate,
                statement.Reference ??
                $"BANK-{statement.Id.ToString()[..8]}",
                $"Coded from bank statement ({request.VatTreatment})",
                lines),
            ct);

        var bankLine = journal.Lines.Single(
            x => x.LedgerAccountId == statement.BankAccountId);

        await reconciliation.ReconcileAsync(
            userId,
            request.OrganisationId,
            statement.Id,
            bankLine.Id,
            ct);

        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = request.OrganisationId,
            UserId = userId,
            EventType = "BankTransactionCoded",
            EntityType = nameof(BankStatementLine),
            EntityId = statement.Id.ToString(),
            JsonData = JsonSerializer.Serialize(new
            {
                statement.Amount,
                request.VatTreatment,
                VatAmount = tax.Vat.Amount,
                TargetAccountId = target.Id,
                target.Code,
                target.Name,
                JournalId = journal.Id
            })
        });

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return journal;
    }

    private async Task<PostedJournal> PostOrMatchInternalTransferAsync(
        string userId,
        BankTransactionCodingRequest request,
        BankStatementLine statement,
        string description,
        decimal amount,
        CancellationToken ct)
    {
        var otherBankAccountId =
            request.TransferToBankAccountId!.Value;

        if (otherBankAccountId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Choose the other bank account for this transfer.");
        }

        if (otherBankAccountId == statement.BankAccountId)
        {
            throw new InvalidOperationException(
                "Choose a different bank account for this transfer.");
        }

        var bankAccountIds = new[]
        {
            statement.BankAccountId,
            otherBankAccountId
        };

        var bankAccounts = await db.LedgerAccounts
            .Where(x =>
                x.OrganisationId == request.OrganisationId &&
                x.IsActive &&
                x.IsBankAccount &&
                bankAccountIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        if (bankAccounts.Count != 2)
        {
            throw new InvalidOperationException(
                "Both sides of the transfer must be active bank accounts.");
        }

        var fromBankAccountId =
            statement.Amount > 0
                ? otherBankAccountId
                : statement.BankAccountId;

        var toBankAccountId =
            statement.Amount > 0
                ? statement.BankAccountId
                : otherBankAccountId;

        /*
         * First check whether the opposite statement side has already
         * created this transfer.
         *
         * We allow a small date window because different banks can post
         * the two sides on different statement dates.
         */
        var earliestDate = statement.TransactionDate.AddDays(-3);
        var latestDate = statement.TransactionDate.AddDays(3);

        var possibleTransfers = await db.BankTransfers
            .AsNoTracking()
            .Where(x =>
                x.OrganisationId == request.OrganisationId &&
                x.FromBankAccountId == fromBankAccountId &&
                x.ToBankAccountId == toBankAccountId &&
                x.Amount == amount &&
                x.TransferDate >= earliestDate &&
                x.TransferDate <= latestDate)
            .OrderBy(x => x.TransferDate)
            .ToListAsync(ct);

        var availableMatches = new List<TransferMatch>();

        foreach (var transfer in possibleTransfers)
        {
            var journal = await db.PostedJournals
                .AsNoTracking()
                .Include(x => x.Lines)
                .SingleOrDefaultAsync(
                    x => x.Id == transfer.PostedJournalId &&
                         x.OrganisationId ==
                         request.OrganisationId,
                    ct);

            if (journal is null)
            {
                continue;
            }

            var bankLine = journal.Lines.SingleOrDefault(
                x => x.LedgerAccountId ==
                     statement.BankAccountId);

            if (bankLine is null)
            {
                continue;
            }

            var alreadyReconciled =
                await db.BankStatementLines
                    .AnyAsync(
                        x => x.MatchedPostedJournalLineId ==
                             bankLine.Id,
                        ct);

            if (!alreadyReconciled)
            {
                availableMatches.Add(
                    new TransferMatch(
                        transfer,
                        journal,
                        bankLine));
            }
        }

        if (availableMatches.Count > 1)
        {
            throw new InvalidOperationException(
                "More than one existing internal transfer matches this statement line. Review the transfer date and amount before reconciling.");
        }

        /*
         * Existing transfer found:
         * reconcile this statement line against the unused opposite
         * bank line instead of posting a second journal.
         */
        if (availableMatches.Count == 1)
        {
            var match = availableMatches[0];

            await using var matchTransaction =
                await db.Database.BeginTransactionAsync(ct);

            await reconciliation.ReconcileAsync(
                userId,
                request.OrganisationId,
                statement.Id,
                match.BankLine.Id,
                ct);

            db.AuditEvents.Add(new AuditEvent
            {
                OrganisationId = request.OrganisationId,
                UserId = userId,
                EventType = "BankTransferMatched",
                EntityType = nameof(BankStatementLine),
                EntityId = statement.Id.ToString(),
                JsonData = JsonSerializer.Serialize(new
                {
                    BankTransferId = match.Transfer.Id,
                    JournalId = match.Journal.Id,
                    JournalLineId = match.BankLine.Id,
                    statement.Amount,
                    FromBankAccountId = fromBankAccountId,
                    ToBankAccountId = toBankAccountId
                })
            });

            await db.SaveChangesAsync(ct);
            await matchTransaction.CommitAsync(ct);

            return match.Journal;
        }

        /*
         * No existing transfer:
         * this is the first statement side, so create the journal and
         * transfer record.
         */
        var lines = new List<JournalLineInput>
        {
            new(
                toBankAccountId,
                description,
                amount,
                0),

            new(
                fromBankAccountId,
                description,
                0,
                amount)
        };

        await using var transaction =
            await db.Database.BeginTransactionAsync(ct);

        var journalCreated = await posting.PostAsync(
            userId,
            new(
                request.OrganisationId,
                statement.TransactionDate,
                statement.Reference ??
                $"BANK-{statement.Id.ToString()[..8]}",
                "Coded from bank statement (Internal transfer)",
                lines),
            ct);

        var transferCreated = new BankTransfer
        {
            OrganisationId = request.OrganisationId,
            FromBankAccountId = fromBankAccountId,
            ToBankAccountId = toBankAccountId,
            TransferDate = statement.TransactionDate,
            Reference =
                $"BANK-TRF-{statement.Id.ToString("N")[..8]}",
            Description = description,
            Amount = amount,
            PostedJournalId = journalCreated.Id,
            CreatedByUserId = userId
        };

        db.BankTransfers.Add(transferCreated);

        var currentBankLine = journalCreated.Lines.Single(
            x => x.LedgerAccountId == statement.BankAccountId);

        await reconciliation.ReconcileAsync(
            userId,
            request.OrganisationId,
            statement.Id,
            currentBankLine.Id,
            ct);

        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = request.OrganisationId,
            UserId = userId,
            EventType = "BankTransactionTransferCoded",
            EntityType = nameof(BankStatementLine),
            EntityId = statement.Id.ToString(),
            JsonData = JsonSerializer.Serialize(new
            {
                BankTransferId = transferCreated.Id,
                JournalId = journalCreated.Id,
                statement.Amount,
                FromBankAccountId = fromBankAccountId,
                ToBankAccountId = toBankAccountId
            })
        });

        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return journalCreated;
        }
        catch (DbUpdateException ex)
        {
            throw new InvalidOperationException(
                "The internal bank transfer could not be saved because one of its related accounting records is no longer valid. Refresh the banking page and try again.",
                ex);
        }
    }

    private async Task<Guid> ControlAccountId(
    Guid organisationId,
    string code,
    string name,
    CancellationToken ct)
{
    var expectedType =
        code switch
        {
            "1150" => AccountType.Asset,
            "2100" => AccountType.Liability,
            _ => throw new InvalidOperationException(
                $"Unsupported VAT control account {code}.")
        };

    var account =
        await db.LedgerAccounts
            .SingleOrDefaultAsync(
                x =>
                    x.OrganisationId == organisationId &&
                    x.Code == code &&
                    x.IsActive,
                ct);

    if (account is null ||
        account.Type != expectedType)
    {
        throw new InvalidOperationException(
            $"{name} ({code}) must be an active {expectedType} account.");
    }

    return account.Id;
}

    private sealed record TransferMatch(
        BankTransfer Transfer,
        PostedJournal Journal,
        PostedJournalLine BankLine);
}