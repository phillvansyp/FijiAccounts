using System.Data;
using System.Text.Json;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed class ReceiptBankPurchaseService(
    ApplicationDbContext db,
    EmployeeReceiptService receipts,
    BankTransactionCodingService bankCoding,
    BankReconciliationService reconciliation,
    PurchasingService purchasing,
    TenantAccessService access)
{
    public async Task<SupplierBill> ApprovePostAndPayAsync(
        string userId, Guid organisationId, Guid receiptId, int receiptVersion,
        Guid statementLineId, Guid supplierId, Guid expenseAccountId,
        VatTreatment vatTreatment, CancellationToken ct = default)
    {
        if (!await receipts.IsOwnerAsync(userId, organisationId) ||
            !await access.CanPostJournalsAsync(userId, organisationId))
            throw new UnauthorizedAccessException("You need receipt review and posting access to complete this purchase.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var receipt = await db.EmployeeReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
                x.Id == receiptId && x.OrganisationId == organisationId, ct)
                ?? throw new InvalidOperationException("Receipt not found.");
            if (receipt.Status != "Submitted" || receipt.Version != receiptVersion ||
                receipt.LinkedSupplierBillId is not null || receipt.LinkedSupplierBillDraftId is not null)
                throw new InvalidOperationException("This receipt has changed. Refresh it before posting.");
            if (receipt.PaidPersonally)
                throw new InvalidOperationException("A company bank payment cannot settle a personally paid receipt.");

            var organisation = await db.Organisations.AsNoTracking().SingleAsync(x => x.Id == organisationId, ct);
            if (receipt.Currency != organisation.BaseCurrency)
                throw new InvalidOperationException("The receipt and bank payment must use the organisation's base currency.");
            if (organisation.RequireSupplierPaymentApproval)
                throw new InvalidOperationException("This company requires independent payment approval. Create the purchase draft and use the payment approval workflow.");
            if (!await db.BusinessParties.AnyAsync(x => x.Id == supplierId && x.OrganisationId == organisationId &&
                x.IsActive && (x.Type & PartyType.Supplier) != 0, ct))
                throw new InvalidOperationException("Choose an active supplier for this bill.");
            if (!await db.LedgerAccounts.AnyAsync(x => x.Id == expenseAccountId && x.OrganisationId == organisationId &&
                x.IsActive && !x.IsBankAccount && x.Code != "1150" &&
                (x.Type == AccountType.Expense || x.Type == AccountType.Asset), ct))
                throw new InvalidOperationException("Choose an active expense or asset account.");

            var statement = await db.BankStatementLines.AsNoTracking().SingleOrDefaultAsync(x =>
                x.Id == statementLineId && x.OrganisationId == organisationId, ct)
                ?? throw new InvalidOperationException("Bank transaction not found.");
            if (statement.Amount >= 0 || Math.Abs(statement.Amount + receipt.TotalPaid) > 0.01m ||
                Math.Abs(statement.TransactionDate.DayNumber - receipt.ReceiptDate.DayNumber) > 7)
                throw new InvalidOperationException("The selected bank transaction no longer matches the receipt's amount and date.");
            if (receipt.ReceiptDate > statement.TransactionDate.AddDays(3))
                throw new InvalidOperationException("The receipt is dated after the bank payment. Review the dates before posting this purchase.");
            var earliestBillDate = receipt.ReceiptDate.AddDays(-7);
            var latestBillDate = receipt.ReceiptDate.AddDays(7);
            if (await db.SupplierBills.AnyAsync(x => x.OrganisationId == organisationId &&
                x.SupplierId == supplierId && x.Status != BillStatus.Voided &&
                x.Currency == receipt.Currency && x.TransactionTotal == receipt.TotalPaid &&
                x.BillDate >= earliestBillDate && x.BillDate <= latestBillDate, ct))
                throw new InvalidOperationException("A bill for this supplier, amount and date already exists. Attach the receipt to that bill instead of posting a duplicate.");
            if (await reconciliation.IsInsideCompletedReconciliationAsync(
                organisationId, statement.BankAccountId, statement.TransactionDate, ct))
                throw new InvalidOperationException("This bank transaction is in a completed reconciliation period. It cannot be recoded automatically; review it in Banking.");

            if (statement.ReconciledAt is not null)
            {
                var matched = await db.PostedJournalLines.AsNoTracking()
                    .Where(x => x.Id == statement.MatchedPostedJournalLineId)
                    .Select(x => new { x.PostedJournalId, x.PostedJournal.Description })
                    .SingleOrDefaultAsync(ct);
                if (matched?.Description?.StartsWith("Coded from bank statement", StringComparison.OrdinalIgnoreCase) != true)
                    throw new InvalidOperationException("This payment is reconciled to another document or journal. Review its Connections in Banking before using it for a new bill.");
                if (await db.BankStatementAdditionalMatches.AnyAsync(x => x.BankStatementLineId == statementLineId, ct) ||
                    await db.BankStatementLines.AnyAsync(x => x.OrganisationId == organisationId && x.Id != statementLineId &&
                        x.MatchedPostedJournalLine != null && x.MatchedPostedJournalLine.PostedJournalId == matched.PostedJournalId, ct))
                    throw new InvalidOperationException("This bank coding has multiple matches. Review it in Banking before changing it.");
            }

            // All three postings share one transaction. If coding, bill posting, or payment fails,
            // the original reconciliation and every ledger entry stay as they were.
            if (statement.ReconciledAt is not null &&
                !await bankCoding.ReopenCodingAsync(userId, organisationId, statementLineId, ct))
                throw new InvalidOperationException("This reconciliation is not a bank-coded expense and cannot be replaced automatically.");

            await receipts.ReviewAsync(userId, organisationId, receiptId, receiptVersion,
                approve: true, note: "Matched to confirmed bank transaction", createDraft: true);
            var draft = await db.SupplierBillDrafts.AsNoTracking().SingleAsync(x =>
                x.OrganisationId == organisationId &&
                db.EmployeeReceipts.Any(r => r.Id == receiptId && r.LinkedSupplierBillDraftId == x.Id), ct);
            var attachment = new SupplierBillAttachmentRequest(
                draft.AttachmentFileName!, draft.AttachmentContentType!,
                draft.AttachmentOriginalSize!.Value, draft.AttachmentContent!, draft.AttachmentIsCompressed);
            var bill = await purchasing.PostDraftBillAsync(userId, draft.Id,
                new SupplierBillRequest(organisationId, supplierId, draft.SupplierReference,
                    receipt.ReceiptDate, draft.DueDate,
                    [new SupplierBillLineRequest(draft.Description, 1, receipt.Amount, vatTreatment, expenseAccountId)],
                    draft.BranchId, draft.DivisionId, receipt.AmountsIncludeVat, receipt.Currency, 1m),
                attachment, ct);
            if (Math.Abs(bill.TransactionTotal - receipt.TotalPaid) > 0.01m)
                throw new InvalidOperationException("The selected VAT treatment does not reproduce the amount paid on the receipt. Check VAT before posting.");

            await purchasing.PayBillAsync(userId, new SupplierPaymentRequest(
                organisationId, bill.Id, statement.TransactionDate,
                string.IsNullOrWhiteSpace(statement.Reference) ? draft.SupplierReference : statement.Reference,
                receipt.TotalPaid, statement.BankAccountId, statementLineId), ct);
            db.AuditEvents.Add(new AuditEvent {
                OrganisationId = organisationId, UserId = userId,
                EventType = "ReceiptBankPurchaseCompleted", EntityType = nameof(EmployeeReceipt),
                EntityId = receiptId.ToString(),
                JsonData = JsonSerializer.Serialize(new { BillId = bill.Id, statementLineId })
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return bill;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
