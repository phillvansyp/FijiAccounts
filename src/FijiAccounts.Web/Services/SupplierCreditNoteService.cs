using System.Data;
using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record SupplierCreditNoteRequest(Guid OrganisationId, Guid SupplierBillId, DateOnly Date, string Reason, decimal Amount, bool ReturnTrackedItems);

public sealed class SupplierCreditNoteService(ApplicationDbContext db, TenantAccessService access, JournalPostingService posting)
{
    public async Task<SupplierCreditNote> CreateAsync(string userId, SupplierCreditNoteRequest request, CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, request.OrganisationId)) throw new UnauthorizedAccessException("You cannot issue supplier credit notes for this organisation.");
        var organisation = await db.Organisations.SingleAsync(x => x.Id == request.OrganisationId, ct);
        if (!string.Equals(organisation.CountryCode, "FJ", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Only the verified Fiji supplier credit process is enabled at this stage.");
        var bill = await db.SupplierBills.Include(x => x.Lines).ThenInclude(x => x.ProductItem).SingleOrDefaultAsync(x => x.Id == request.SupplierBillId && x.OrganisationId == request.OrganisationId, ct) ?? throw new InvalidOperationException("Supplier bill not found.");
        if (bill.Status is BillStatus.Voided or BillStatus.Credited) throw new InvalidOperationException("This supplier bill cannot be credited.");
        var available = bill.Total - bill.AmountPaid - bill.AmountCredited;
        if (request.Amount <= 0 || request.Amount > available) throw new InvalidOperationException($"Credit must be between $0.01 and ${available:N2}.");
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException("Enter the supplier credit note reference or reason.");
        var trackedLines = bill.Lines.Where(x => x.ProductItem?.Kind == ProductKind.TrackedItem).ToList();
        if (trackedLines.Count > 0 && !request.ReturnTrackedItems) throw new InvalidOperationException("This bill contains tracked stock. Confirm that the proportional stock is being returned to the supplier.");

        var ratio = request.Amount / bill.Total;
        var net = decimal.Round(bill.Subtotal * ratio, 2, MidpointRounding.AwayFromZero);
        var vat = request.Amount - net;
        var controls = await db.LedgerAccounts.Where(x => x.OrganisationId == request.OrganisationId && (x.Code == "1150" || x.Code == "2000")).ToDictionaryAsync(x => x.Code, ct);
        if (!controls.ContainsKey("1150") || !controls.ContainsKey("2000")) throw new InvalidOperationException("VAT Receivable (1150) and Accounts Payable (2000) are required.");
        var receipts = trackedLines.Count == 0 ? [] : await db.InventoryMovements.Where(x => x.OrganisationId == request.OrganisationId && x.Reference == bill.BillNumber && x.QuantityChange > 0).ToListAsync(ct);
        foreach (var receipt in receipts)
        {
            var item = trackedLines.Select(x => x.ProductItem).First(x => x?.Id == receipt.ProductItemId)!;
            var quantity = decimal.Round(receipt.QuantityChange * ratio, 4, MidpointRounding.AwayFromZero);
            if (item.QuantityOnHand < quantity) throw new InvalidOperationException($"Cannot return {quantity:N4} units of {item.Code}; only {item.QuantityOnHand:N4} are on hand.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var sequence = (await db.SupplierCreditNotes.Where(x => x.OrganisationId == request.OrganisationId).MaxAsync(x => (long?)x.SequenceNumber, ct) ?? 0) + 1;
        var number = $"SCN-{sequence:D6}";
        var journalLines = bill.Lines.GroupBy(x => x.ExpenseAccountId).Select(x => new JournalLineInput(x.Key, number, 0, decimal.Round(x.Sum(y => y.NetAmount) * ratio, 2, MidpointRounding.AwayFromZero))).ToList();
        var allocatedNet = journalLines.Sum(x => x.Credit);
        if (journalLines.Count > 0 && allocatedNet != net) journalLines[0] = journalLines[0] with { Credit = journalLines[0].Credit + net - allocatedNet };
        journalLines.Add(new(controls["2000"].Id, number, request.Amount, 0));
        if (vat > 0) journalLines.Add(new(controls["1150"].Id, number, 0, vat));
        var journal = await posting.PostAsync(userId, new(request.OrganisationId, request.Date, number, $"Supplier credit for {bill.BillNumber}: {request.Reason.Trim()}", journalLines), ct);

        foreach (var receipt in receipts)
        {
            var item = trackedLines.Select(x => x.ProductItem).First(x => x?.Id == receipt.ProductItemId)!;
            var quantity = decimal.Round(receipt.QuantityChange * ratio, 4, MidpointRounding.AwayFromZero);
            var value = decimal.Round(receipt.ValueChange * ratio, 2, MidpointRounding.AwayFromZero);
            var oldValue = decimal.Round(item.QuantityOnHand * item.AverageCost, 2, MidpointRounding.AwayFromZero);
            item.QuantityOnHand -= quantity;
            item.AverageCost = item.QuantityOnHand == 0 ? 0 : decimal.Round((oldValue - value) / item.QuantityOnHand, 4, MidpointRounding.AwayFromZero);
            db.InventoryMovements.Add(new InventoryMovement { OrganisationId = request.OrganisationId, ProductItemId = item.Id, MovementDate = request.Date, Type = InventoryMovementType.PurchaseReturn, QuantityChange = -quantity, UnitCost = receipt.UnitCost, ValueChange = -value, Reference = number, Note = $"Return to supplier against {bill.BillNumber}", PostedJournalId = journal.Id, PostedByUserId = userId });
        }

        var credit = new SupplierCreditNote { OrganisationId = request.OrganisationId, SupplierBillId = bill.Id, SequenceNumber = sequence, CreditNoteNumber = number, CreditDate = request.Date, Reason = request.Reason.Trim(), Currency = bill.Currency, Subtotal = net, VatTotal = vat, Total = request.Amount, ReturnedTrackedItems = receipts.Count > 0, PostedJournalId = journal.Id, CreatedByUserId = userId };
        bill.AmountCredited += request.Amount;
        bill.Status = bill.Total - bill.AmountPaid - bill.AmountCredited <= 0 ? BillStatus.Credited : BillStatus.PartPaid;
        db.SupplierCreditNotes.Add(credit);
        db.AuditEvents.Add(new AuditEvent { OrganisationId = request.OrganisationId, UserId = userId, EventType = "SupplierCreditNotePosted", EntityType = nameof(SupplierCreditNote), EntityId = credit.Id.ToString(), JsonData = JsonSerializer.Serialize(new { credit.CreditNoteNumber, bill.BillNumber, credit.Total, credit.VatTotal, credit.ReturnedTrackedItems }) });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return credit;
    }
}
