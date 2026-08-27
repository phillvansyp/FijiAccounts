using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record CustomerReceiptRequest(
    Guid OrganisationId,
    Guid SalesInvoiceId,
    DateOnly Date,
    string Reference,
    decimal Amount,
    Guid BankAccountId,
    Guid? StatementLineId = null,
    decimal? TransactionAmount = null);

public sealed class CustomerReceiptService(
    ApplicationDbContext db,
    TenantAccessService access,
    JournalPostingService posting,
    BankReconciliationService reconciliation,
    NotificationService notifications)
{
    public async Task<CustomerReceipt> RecordAsync(string userId, CustomerReceiptRequest request, CancellationToken cancellationToken = default)
    {
        if (!await access.CanPostJournalsAsync(userId, request.OrganisationId)) throw new UnauthorizedAccessException("You cannot record receipts for this organisation.");
        if (request.Amount <= 0) throw new InvalidOperationException("Receipt amount must be greater than zero.");
        BankStatementLine? statement = null;
        if (request.StatementLineId is Guid statementLineId)
        {
            statement = await db.BankStatementLines.SingleOrDefaultAsync(
                x => x.Id == statementLineId && x.OrganisationId == request.OrganisationId,
                cancellationToken) ?? throw new InvalidOperationException("Bank statement line not found.");
            if (statement.ReconciledAt is not null)
            {
                throw new InvalidOperationException("This bank statement line is already reconciled.");
            }
            if (statement.BankAccountId != request.BankAccountId || statement.TransactionDate != request.Date)
            {
                throw new InvalidOperationException("The customer receipt must use the bank account and date from the statement line.");
            }
            var statementReceiptAmount = Math.Round(statement.Amount, 2, MidpointRounding.AwayFromZero);
            if (statement.Amount <= 0 || Math.Abs(statementReceiptAmount - request.Amount) > 0.01m)
            {
                throw new InvalidOperationException("The customer receipt must exactly match the incoming statement amount.");
            }
        }
        var invoice = await db.SalesInvoices.Include(x => x.Customer).Include(x => x.Organisation).SingleOrDefaultAsync(x => x.Id == request.SalesInvoiceId && x.OrganisationId == request.OrganisationId, cancellationToken) ?? throw new InvalidOperationException("Invoice not found in this organisation.");
        if (invoice.Status is InvoiceStatus.Voided or InvoiceStatus.Draft or InvoiceStatus.Credited or InvoiceStatus.Paid) throw new InvalidOperationException("Only outstanding posted invoices can receive payments.");
        var outstanding = invoice.Total - invoice.AmountPaid - invoice.AmountCredited;
        var isBaseCurrency = string.Equals(invoice.Currency, invoice.Organisation?.BaseCurrency ?? "FJD", StringComparison.OrdinalIgnoreCase);
        var transactionAmount = request.TransactionAmount ?? (isBaseCurrency ? request.Amount : 0m);
        if (transactionAmount <= 0)
        {
            throw new InvalidOperationException($"Enter the amount received in {invoice.Currency}.");
        }
        var settlement = ForeignCurrencySettlement.Calculate(
            transactionAmount,
            invoice.ExchangeRateToBase,
            request.Amount);
        if (settlement.DocumentBaseAmount > outstanding + 0.01m)
        {
            throw new InvalidOperationException(
                $"Receipt exceeds the outstanding balance of {invoice.Currency} {(outstanding / invoice.ExchangeRateToBase):N2}.");
        }
        var carryingAmount = Math.Min(outstanding, settlement.DocumentBaseAmount);
        var realisedDifference = request.Amount - carryingAmount;
        var bank = await db.LedgerAccounts.SingleOrDefaultAsync(x => x.Id == request.BankAccountId && x.OrganisationId == request.OrganisationId && x.IsActive && x.IsBankAccount, cancellationToken) ?? throw new InvalidOperationException("Select an active bank account.");
        var receivable =
    await db.LedgerAccounts.SingleOrDefaultAsync(
        x =>
            x.OrganisationId == request.OrganisationId &&
            x.Code == "1100" &&
            x.IsActive,
        cancellationToken);

if (receivable is null ||
    receivable.Type != AccountType.Asset)
{
    throw new InvalidOperationException(
        "Accounts Receivable (1100) must be an active Asset account.");
}

        LedgerAccount? exchangeAccount = null;
        if (realisedDifference != 0)
        {
            var accountCode = realisedDifference > 0 ? "4300" : "6950";
            exchangeAccount = await db.LedgerAccounts.SingleOrDefaultAsync(
                x => x.OrganisationId == request.OrganisationId && x.Code == accountCode && x.IsActive,
                cancellationToken) ?? throw new InvalidOperationException(
                    $"Foreign Exchange {(realisedDifference > 0 ? "Gains (4300)" : "Losses (6950)")} must be active.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var lines = new List<JournalLineInput>
        {
            new(bank.Id, invoice.InvoiceNumber, request.Amount, 0),
            new(receivable.Id, invoice.InvoiceNumber, 0, carryingAmount)
        };
        if (realisedDifference > 0)
        {
            lines.Add(new(exchangeAccount!.Id, $"FX gain {invoice.InvoiceNumber}", 0, realisedDifference));
        }
        else if (realisedDifference < 0)
        {
            lines.Add(new(exchangeAccount!.Id, $"FX loss {invoice.InvoiceNumber}", -realisedDifference, 0));
        }
        var journal = await posting.PostAsync(userId, new JournalPostRequest(request.OrganisationId, request.Date, request.Reference, $"Receipt for {invoice.InvoiceNumber}", lines, invoice.BranchId, invoice.DivisionId), cancellationToken);
        var receipt = new CustomerReceipt { OrganisationId = request.OrganisationId, BranchId = invoice.BranchId, DivisionId = invoice.DivisionId, CustomerId = invoice.CustomerId, ReceiptDate = request.Date, Reference = request.Reference.Trim(), Amount = request.Amount, Currency = invoice.Currency, TransactionAmount = transactionAmount, ExchangeRateToBase = settlement.SettlementRateToBase, RealisedExchangeDifference = realisedDifference, BankAccountId = bank.Id, PostedJournalId = journal.Id, CreatedByUserId = userId };
        receipt.Allocations.Add(new CustomerReceiptAllocation { SalesInvoiceId = invoice.Id, TransactionAmount = transactionAmount, Amount = carryingAmount });
        invoice.TransactionAmountPaid += transactionAmount;
        invoice.AmountPaid += carryingAmount;
        invoice.Status = invoice.Total - invoice.AmountPaid - invoice.AmountCredited <= 0.01m ? InvoiceStatus.Paid : InvoiceStatus.PartPaid;

        if (invoice.Status == InvoiceStatus.Paid)
        {
            await notifications.ResolveSalesInvoiceNotificationsAsync(
                request.OrganisationId,
                invoice.Id,
                publishUpdate: false,
                ct: cancellationToken);
        }
        db.CustomerReceipts.Add(receipt);
        db.AuditEvents.Add(new AuditEvent { OrganisationId = request.OrganisationId, EventType = "CustomerReceiptRecorded", EntityType = nameof(CustomerReceipt), EntityId = receipt.Id.ToString(), UserId = userId, JsonData = JsonSerializer.Serialize(new { invoice.InvoiceNumber, receipt.Reference, receipt.Currency, receipt.TransactionAmount, receipt.Amount, receipt.ExchangeRateToBase, receipt.RealisedExchangeDifference }) });
        if (statement is not null)
        {
            var bankJournalLine = journal.Lines.Single(x => x.LedgerAccountId == bank.Id);
            await reconciliation.ReconcileAsync(
                userId,
                request.OrganisationId,
                statement.Id,
                bankJournalLine.Id,
                cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        notifications.PublishOrganisationUpdate(request.OrganisationId);
        return receipt;
    }

    public async Task<CustomerReceiptReversal> ReverseAsync(string userId, Guid organisationId, Guid receiptId, DateOnly reversalDate, string reason, CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId)) throw new UnauthorizedAccessException("You cannot reverse customer receipts for this organisation.");
        if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("Enter a reason for reversing the receipt.");
        var receipt = await db.CustomerReceipts.Include(x => x.Allocations).ThenInclude(x => x.SalesInvoice).SingleOrDefaultAsync(x => x.Id == receiptId && x.OrganisationId == organisationId, ct) ?? throw new InvalidOperationException("Customer receipt not found.");
        if (await db.CustomerReceiptReversals.AnyAsync(x => x.CustomerReceiptId == receiptId, ct)) throw new InvalidOperationException("This receipt has already been reversed.");
        if (receipt.Allocations.Any(x => x.SalesInvoice.Status == InvoiceStatus.Voided))
{
    throw new InvalidOperationException(
        "A receipt allocated to a voided invoice cannot be reversed here.");
}

var completedReconciliationExists =
    await reconciliation.IsInsideCompletedReconciliationAsync(
        organisationId,
        receipt.BankAccountId,
        receipt.ReceiptDate,
        ct);

if (completedReconciliationExists)
{
    throw new InvalidOperationException(
        "A customer receipt inside a completed bank reconciliation period cannot be reversed.");
}

await using var transaction =
    await db.Database.BeginTransactionAsync(
        IsolationLevel.Serializable,
        ct);
        var original = await db.PostedJournals.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == receipt.PostedJournalId && x.OrganisationId == organisationId, ct);
        var reference = $"REV-{receipt.Reference}"; var lines = original.Lines.Select(x => new JournalLineInput(x.LedgerAccountId, $"Reverse receipt {receipt.Reference}", x.Credit, x.Debit, x.BranchId, x.DivisionId, x.ProjectId, x.ProjectCostCodeId)).ToList();
        var journal = await posting.PostAsync(userId, new(organisationId, reversalDate, reference, $"Reverse customer receipt: {reason.Trim()}", lines), ct);
        foreach (var allocation in receipt.Allocations) { var invoice = allocation.SalesInvoice; invoice.AmountPaid -= allocation.Amount; invoice.TransactionAmountPaid -= allocation.TransactionAmount; if (invoice.AmountPaid < 0 || invoice.TransactionAmountPaid < 0) throw new InvalidOperationException("Receipt allocation history is inconsistent and cannot be reversed."); var outstanding = invoice.Total - invoice.AmountPaid - invoice.AmountCredited; invoice.Status = outstanding <= 0 ? (invoice.AmountCredited > 0 ? InvoiceStatus.Credited : InvoiceStatus.Paid) : invoice.AmountPaid > 0 || invoice.AmountCredited > 0 ? InvoiceStatus.PartPaid : InvoiceStatus.Posted; }
        var reversal = new CustomerReceiptReversal { OrganisationId = organisationId, CustomerReceiptId = receipt.Id, ReversalDate = reversalDate, Reason = reason.Trim(), PostedJournalId = journal.Id, CreatedByUserId = userId }; db.CustomerReceiptReversals.Add(reversal); db.AuditEvents.Add(new AuditEvent { OrganisationId = organisationId, EventType = "CustomerReceiptReversed", EntityType = nameof(CustomerReceiptReversal), EntityId = reversal.Id.ToString(), UserId = userId, JsonData = JsonSerializer.Serialize(new { receipt.Reference, receipt.Amount, reason, ReversalJournalId = journal.Id }) }); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); notifications.PublishOrganisationUpdate(organisationId); return reversal;
    }
}
