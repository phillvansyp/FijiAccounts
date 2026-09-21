using System.Data;
using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed class InvoiceCorrectionService(ApplicationDbContext db, TenantAccessService access,
    SalesInvoiceService sales, PurchasingService purchases)
{
    public async Task<SalesInvoice> CorrectSalesAsync(string userId, Guid invoiceId, SalesInvoiceRequest request, string reason, CancellationToken ct = default)
    {
        await RequirePermission(userId, request.OrganisationId, reason);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var original = await db.SalesInvoices.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == invoiceId && x.OrganisationId == request.OrganisationId, ct)
            ?? throw new InvalidOperationException("Invoice not found.");
        await RequireDimension(userId, request.OrganisationId, original.BranchId, original.DivisionId, ct);
        if (original.Status != InvoiceStatus.Posted || original.AmountPaid != 0 || original.AmountCredited != 0)
            throw new InvalidOperationException("Only unpaid posted invoices can be edited here. Use the payment or credit correction workflow for this invoice.");
        if (await db.CustomerReceiptAllocations.AnyAsync(x => x.SalesInvoiceId == invoiceId, ct) ||
            await db.SalesCreditNotes.AnyAsync(x => x.SalesInvoiceId == invoiceId, ct))
            throw new InvalidOperationException("This invoice has payment or credit history. Use its payment or credit correction workflow.");
        if (await db.ProjectProgressClaims.AnyAsync(x => x.SalesInvoiceId == invoiceId, ct))
            throw new InvalidOperationException("Edit this invoice through its linked project progress claim.");
        if (await db.FiscalisationRecords.AnyAsync(x => x.SalesInvoiceId == invoiceId, ct))
            throw new InvalidOperationException("This invoice has fiscal records. Use its fiscal credit and replacement workflow.");

        var before = JsonSerializer.Serialize(new { original.InvoiceNumber, original.IssueDate, original.DueDate,
            original.CustomerId, original.Currency, original.ExchangeRateToBase, original.Total, original.VatTotal,
            Lines = original.Lines.Select(x => new { x.Description, x.Quantity, x.TransactionUnitPrice, x.VatTreatment }) });
        await sales.UpdatePostedAsync(userId, original, request, ct);
        db.AuditEvents.Add(new AuditEvent { OrganisationId = request.OrganisationId, UserId = userId,
            EntityType = nameof(SalesInvoice), EntityId = original.Id.ToString(), EventType = "SalesInvoiceUpdated",
            JsonData = JsonSerializer.Serialize(new { Before = before, original.Total, original.VatTotal, Reason = reason.Trim() }) });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return original;
    }

    public async Task<SupplierBill> CorrectPurchaseAsync(string userId, Guid billId, SupplierBillRequest request, string reason, CancellationToken ct = default)
    {
        await RequirePermission(userId, request.OrganisationId, reason);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var original = await db.SupplierBills.Include(x => x.Attachments).SingleOrDefaultAsync(x => x.Id == billId && x.OrganisationId == request.OrganisationId, ct)
            ?? throw new InvalidOperationException("Supplier bill not found.");
        await RequireDimension(userId, request.OrganisationId, original.BranchId, original.DivisionId, ct);
        if (original.Status != BillStatus.Posted || original.AmountPaid != 0 || original.AmountCredited != 0)
            throw new InvalidOperationException("Only unpaid posted bills can be edited here. Use the payment or credit correction workflow for this bill.");
        if (await db.PurchaseOrders.AnyAsync(x => x.SupplierBillId == billId, ct))
            throw new InvalidOperationException("This bill is linked to a purchase order. Use the purchase order correction and approval workflow.");
        if (await db.SupplierPaymentApprovals.AnyAsync(x => x.SupplierBillId == billId && x.Status == SupplierPaymentApprovalStatus.Pending, ct))
            throw new InvalidOperationException("Withdraw the pending payment request before editing this bill.");

        await purchases.VoidBillAsync(userId, request.OrganisationId, billId, original.BillDate, reason, ct);
        var replacement = await purchases.PostBillAsync(userId, request, ct);
        foreach (var attachment in original.Attachments)
        {
            db.SupplierBillAttachments.Add(new SupplierBillAttachment
            {
                OrganisationId = request.OrganisationId, SupplierBillId = replacement.Id,
                FileName = attachment.FileName, ContentType = attachment.ContentType,
                OriginalSize = attachment.OriginalSize, StoredSize = attachment.StoredSize,
                Content = attachment.Content, IsCompressed = attachment.IsCompressed,
                ImmutableDocumentObjectId = attachment.ImmutableDocumentObjectId,
                UploadedByUserId = userId
            });
        }
        AddHistory(userId, request.OrganisationId, false, original.Id, original.BillNumber, replacement.Id, replacement.BillNumber, reason);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return replacement;
    }

    private async Task RequirePermission(string userId, Guid organisationId, string reason)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId)) throw new UnauthorizedAccessException("You cannot edit invoices for this organisation.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 300) throw new InvalidOperationException("Enter a reason for the edit, up to 300 characters.");
    }

    private async Task RequireDimension(string userId, Guid organisationId, Guid? branchId, Guid? divisionId, CancellationToken ct)
    {
        if (branchId is null || divisionId is null || !await access.CanAccessDimensionAsync(userId, organisationId, branchId.Value, divisionId.Value, ct))
            throw new UnauthorizedAccessException("You do not have access to this invoice's branch and division.");
    }

    private void AddHistory(string userId, Guid organisationId, bool isSales, Guid originalId, string originalNumber, Guid replacementId, string replacementNumber, string reason)
    {
        var entity = isSales ? nameof(SalesInvoice) : nameof(SupplierBill);
        var data = JsonSerializer.Serialize(new InvoiceCorrectionHistory(originalId, originalNumber, replacementId, replacementNumber, reason.Trim()));
        foreach (var id in new[] { originalId, replacementId })
            db.AuditEvents.Add(new AuditEvent { OrganisationId = organisationId, UserId = userId, EntityType = entity,
                EntityId = id.ToString(), EventType = "InvoiceCorrection", JsonData = data });
    }
}

public sealed record InvoiceCorrectionHistory(Guid OriginalId, string OriginalNumber, Guid ReplacementId, string ReplacementNumber, string Reason);
