using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record PaymentDocument(Guid Id, bool IsSales, string Number, string Party, DateOnly Date,
    DateOnly Due, string Currency, decimal Outstanding, string Status, decimal ExchangeRateToBase = 1m)
{
    public decimal OutstandingDocumentAmount => ExchangeRateToBase > 0
        ? decimal.Round(Outstanding / ExchangeRateToBase, 2, MidpointRounding.AwayFromZero) : 0m;
}
public sealed record ConnectedPayment(Guid LineId, Guid BankAccountId, DateOnly Date, decimal Amount, string Reference,
    IReadOnlyList<Guid> DocumentIds, Guid? StatementId);
public sealed record PaymentWorkspace(IReadOnlyList<PaymentDocument> Documents,
    IReadOnlyList<ConnectedPayment> Payments, IReadOnlyList<BankStatementLine> Statements,
    IReadOnlySet<Guid> MissingDocuments);
public sealed record PaymentAllocation(Guid DocumentId, decimal Amount, decimal? TransactionAmount = null);
public sealed record ConnectPaymentsRequest(Guid OrganisationId, Guid StatementId, Guid? ExpectedMatchId,
    IReadOnlyList<Guid> ExistingPaymentLineIds, IReadOnlyList<PaymentAllocation> Allocations);

/// <summary>One transaction for replacing direct coding, recording settlements and matching the bank.</summary>
public sealed class PaymentConnectionsService(ApplicationDbContext db, TenantAccessService access,
    PurchasingService purchasing, CustomerReceiptService receipts, BankTransactionCodingService coding,
    BankReconciliationService reconciliation)
{
    public async Task<PaymentWorkspace> ReadAsync(string userId, Guid organisationId, CancellationToken ct = default)
    {
        if (await access.FindAsync(userId, organisationId) is null) throw new UnauthorizedAccessException();
        var divisions = (await access.ListAccessibleBranchesAsync(userId, organisationId, ct))
            .SelectMany(x => x.Divisions).Select(x => x.Id).ToArray();
        var bills = await db.SupplierBills.AsNoTracking().Include(x => x.Supplier)
            .Where(x => x.OrganisationId == organisationId && x.DivisionId != null && divisions.Contains(x.DivisionId.Value)).ToListAsync(ct);
        var invoices = await db.SalesInvoices.AsNoTracking().Include(x => x.Customer)
            .Where(x => x.OrganisationId == organisationId && x.DivisionId != null && divisions.Contains(x.DivisionId.Value)).ToListAsync(ct);
        var documents = bills.Select(x => new PaymentDocument(x.Id, false, x.BillNumber, x.Supplier.Name,
            x.BillDate, x.DueDate, x.Currency, x.Total - x.AmountPaid - x.AmountCredited, x.Status.ToString(), x.ExchangeRateToBase))
            .Concat(invoices.Select(x => new PaymentDocument(x.Id, true, x.InvoiceNumber, x.Customer.Name,
                x.IssueDate, x.DueDate, x.Currency, x.Total - x.AmountPaid - x.AmountCredited, x.Status.ToString(), x.ExchangeRateToBase))).ToList();
        var excluded = await BankCodingHistory.UnmatchableJournalIdsAsync(db, organisationId, ct);
        var supplierPayments = await db.SupplierPayments.AsNoTracking().Where(x => x.OrganisationId == organisationId &&
            x.DivisionId != null && divisions.Contains(x.DivisionId.Value)).ToListAsync(ct);
        var customerPayments = await db.CustomerReceipts.AsNoTracking().Include(x => x.Allocations).Where(x => x.OrganisationId == organisationId &&
            x.DivisionId != null && divisions.Contains(x.DivisionId.Value)).ToListAsync(ct);
        var sources = supplierPayments.Where(x => !excluded.Contains(x.PostedJournalId)).Select(x =>
            (Journal: x.PostedJournalId, Bank: x.BankAccountId, Documents: (IReadOnlyList<Guid>)[x.SupplierBillId]))
            .Concat(customerPayments.Where(x => !excluded.Contains(x.PostedJournalId)).Select(x =>
                (Journal: x.PostedJournalId, Bank: x.BankAccountId, Documents: (IReadOnlyList<Guid>)x.Allocations.Select(a => a.SalesInvoiceId).ToArray())))
            .ToDictionary(x => x.Journal);
        var journalIds = sources.Keys.ToArray();
        var lines = await db.PostedJournalLines.AsNoTracking().Include(x => x.PostedJournal)
            .Where(x => journalIds.Contains(x.PostedJournalId)).ToListAsync(ct);
        var statements = await db.BankStatementLines.AsNoTracking().Include(x => x.BankAccount)
            .Where(x => x.OrganisationId == organisationId).OrderByDescending(x => x.TransactionDate).ToListAsync(ct);
        var statementIds = statements.Select(x => x.Id).ToArray();
        var additional = await db.BankStatementAdditionalMatches.AsNoTracking()
            .Where(x => statementIds.Contains(x.BankStatementLineId)).ToListAsync(ct);
        var matches = statements.Where(x => x.MatchedPostedJournalLineId != null)
            .Select(x => (Line: x.MatchedPostedJournalLineId!.Value, Statement: x.Id))
            .Concat(additional.Select(x => (Line: x.PostedJournalLineId, Statement: x.BankStatementLineId)))
            .GroupBy(x => x.Line).ToDictionary(x => x.Key, x => x.First().Statement);
        var payments = lines.Where(x => x.LedgerAccountId == sources[x.PostedJournalId].Bank)
            .Select(x => new ConnectedPayment(x.Id, x.LedgerAccountId, x.PostedJournal.EntryDate, x.Debit - x.Credit,
                x.PostedJournal.Reference, sources[x.PostedJournalId].Documents,
                matches.TryGetValue(x.Id, out var id) ? id : null)).ToList();
        var matchedLineIds = matches.Keys.ToArray();
        var matchedLines = await db.PostedJournalLines.AsNoTracking()
            .Where(x => matchedLineIds.Contains(x.Id)).ToListAsync(ct);
        var directJournalIds = matchedLines.Where(x => !sources.ContainsKey(x.PostedJournalId) && !excluded.Contains(x.PostedJournalId))
            .Select(x => x.PostedJournalId).ToArray();
        var codingLines = await db.PostedJournalLines.AsNoTracking().Include(x => x.LedgerAccount)
            .Where(x => directJournalIds.Contains(x.PostedJournalId) && x.DivisionId != null && divisions.Contains(x.DivisionId.Value)).ToListAsync(ct);
        var missingJournals = codingLines.Where(x => PaymentDocumentPolicy.RequiresTradeDocument(x.LedgerAccount))
            .Select(x => x.PostedJournalId).ToHashSet();
        var missing = matchedLines.Where(x => missingJournals.Contains(x.PostedJournalId)).Select(x => matches[x.Id]).ToHashSet();
        return new(documents, payments, statements, missing);
    }

    public async Task ConnectAsync(string userId, ConnectPaymentsRequest request, CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, request.OrganisationId)) throw new UnauthorizedAccessException();
        if (request.Allocations.Count + request.ExistingPaymentLineIds.Count is < 1 or > 100 ||
            request.Allocations.Any(x => x.Amount <= 0 || decimal.Round(x.Amount, 2) != x.Amount) ||
            request.Allocations.Select(x => x.DocumentId).Distinct().Count() != request.Allocations.Count ||
            request.ExistingPaymentLineIds.Distinct().Count() != request.ExistingPaymentLineIds.Count)
            throw new InvalidOperationException("Choose each payment or document once and enter positive amounts with at most two decimal places.");
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        try
        {
            if (await db.PayrollBankMatches.AnyAsync(x => x.OrganisationId == request.OrganisationId && x.BankStatementLineId == request.StatementId, ct))
                throw new InvalidOperationException("This bank transaction is connected to payroll. Review its payroll payment instead.");
            var statement = await db.BankStatementLines.AsNoTracking().SingleAsync(x => x.Id == request.StatementId && x.OrganisationId == request.OrganisationId, ct);
            if (statement.MatchedPostedJournalLineId != request.ExpectedMatchId)
                throw new InvalidOperationException("This transaction has changed. Refresh before continuing.");
            var workspace = await ReadAsync(userId, request.OrganisationId, ct);
            var selected = workspace.Payments.Where(x => request.ExistingPaymentLineIds.Contains(x.LineId)).ToList();
            if (selected.Count != request.ExistingPaymentLineIds.Count || selected.Any(x => x.StatementId != null || x.BankAccountId != statement.BankAccountId))
                throw new InvalidOperationException("An existing payment is unavailable or already matched. Refresh the list.");
            if (request.Allocations.Any(x => !workspace.Documents.Any(d => d.Id == x.DocumentId && d.IsSales == (statement.Amount > 0))))
                throw new InvalidOperationException("Choose accessible documents for this payment direction.");
            if (selected.Any(x => Math.Sign(x.Amount) != Math.Sign(statement.Amount)) ||
                selected.Sum(x => Math.Abs(x.Amount)) + request.Allocations.Sum(x => x.Amount) != Math.Abs(statement.Amount))
                throw new InvalidOperationException("The selected payments and allocations must equal the bank amount exactly.");
            if (statement.ReconciledAt != null)
            {
                var line = await db.PostedJournalLines.AsNoTracking().Include(x => x.PostedJournal)
                    .SingleAsync(x => x.Id == statement.MatchedPostedJournalLineId, ct);
                if (!(line.PostedJournal.Description?.StartsWith("Coded from bank statement", StringComparison.OrdinalIgnoreCase) ?? false))
                    throw new InvalidOperationException("This transaction already has a payment. Open the linked document to correct that payment first.");
                await coding.ReopenCodingAsync(userId, request.OrganisationId, statement.Id, ct);
            }
            var ids = request.ExistingPaymentLineIds.ToList();
            foreach (var allocation in request.Allocations)
            {
                Guid journalId;
                if (statement.Amount < 0)
                    journalId = (await purchasing.PayBillAsync(userId, new(request.OrganisationId, allocation.DocumentId,
                        statement.TransactionDate, statement.Reference ?? "Bank allocation", allocation.Amount, statement.BankAccountId,
                        TransactionAmount: allocation.TransactionAmount), ct)).PostedJournalId;
                else
                    journalId = (await receipts.RecordAsync(userId, new(request.OrganisationId, allocation.DocumentId,
                        statement.TransactionDate, statement.Reference ?? "Bank allocation", allocation.Amount, statement.BankAccountId,
                        TransactionAmount: allocation.TransactionAmount), ct)).PostedJournalId;
                ids.Add(await db.PostedJournalLines.Where(x => x.PostedJournalId == journalId && x.LedgerAccountId == statement.BankAccountId).Select(x => x.Id).SingleAsync(ct));
            }
            await reconciliation.ReconcileManyAsync(userId, request.OrganisationId, statement.Id, ids, ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
