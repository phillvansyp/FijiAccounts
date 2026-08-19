using System.Data;
using System.Text.Json;
using FijiAccounts.Domain.Accounting;
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

    public async Task<SupplierCreditNoteReversal> ReverseAsync(
    string userId,
    Guid organisationId,
    Guid creditNoteId,
    DateOnly reversalDate,
    string reason,
    CancellationToken ct = default)
{
    if (!await access.CanPostJournalsAsync(userId, organisationId))
        throw new UnauthorizedAccessException(
            "You cannot reverse supplier credit notes for this organisation.");

    if (string.IsNullOrWhiteSpace(reason))
        throw new InvalidOperationException(
            "Enter a reason for reversing the supplier credit note.");

    var credit =
        await db.SupplierCreditNotes
            .Include(x => x.SupplierBill)
                .ThenInclude(x => x.Lines)
                    .ThenInclude(x => x.ProductItem)
            .SingleOrDefaultAsync(
                x =>
                    x.Id == creditNoteId &&
                    x.OrganisationId == organisationId,
                ct)
        ?? throw new InvalidOperationException(
            "Supplier credit note not found.");

    if (await db.SupplierCreditNoteReversals.AnyAsync(
            x => x.SupplierCreditNoteId == creditNoteId,
            ct))
    {
        throw new InvalidOperationException(
            "This supplier credit note has already been reversed.");
    }

    var stockReturns =
        await db.InventoryMovements
            .Include(x => x.ProductItem)
            .Where(
                x =>
                    x.OrganisationId == organisationId &&
                    x.PostedJournalId == credit.PostedJournalId &&
                    x.Reference == credit.CreditNoteNumber &&
                    x.Type == InventoryMovementType.PurchaseReturn)
            .ToListAsync(ct);

    await using var transaction =
        await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct);

    var original =
        await db.PostedJournals
            .AsNoTracking()
            .Include(x => x.Lines)
            .SingleAsync(
                x =>
                    x.Id == credit.PostedJournalId &&
                    x.OrganisationId == organisationId,
                ct);

    var reference =
        $"REV-{credit.CreditNoteNumber}";

    var lines =
        original.Lines
            .Select(
                x =>
                    new JournalLineInput(
                        x.LedgerAccountId,
                        $"Reverse {credit.CreditNoteNumber}",
                        x.Credit,
                        x.Debit))
            .ToList();

    var journal =
        await posting.PostAsync(
            userId,
            new JournalPostRequest(
                organisationId,
                reversalDate,
                reference,
                $"Reverse supplier credit note {credit.CreditNoteNumber}: {reason.Trim()}",
                lines),
            ct);

    foreach (var movement in stockReturns)
    {
        var item = movement.ProductItem;

        var quantity =
            -movement.QuantityChange;

        var value =
            -movement.ValueChange;

        item.AverageCost =
            InventoryValuation.WeightedAverage(
                item.QuantityOnHand,
                item.AverageCost,
                quantity,
                movement.UnitCost);

        item.QuantityOnHand += quantity;

        db.InventoryMovements.Add(
            new InventoryMovement
            {
                OrganisationId = organisationId,
                ProductItemId = item.Id,
                MovementDate = reversalDate,
                Type = InventoryMovementType.AdjustmentIncrease,
                QuantityChange = quantity,
                UnitCost = movement.UnitCost,
                ValueChange = value,
                Reference = reference,
                Note =
                    $"Stock restored by reversal of {credit.CreditNoteNumber}",
                PostedJournalId = journal.Id,
                PostedByUserId = userId
            });
    }

    var bill =
        credit.SupplierBill;

    bill.AmountCredited -= credit.Total;

    if (bill.AmountCredited < 0)
    {
        throw new InvalidOperationException(
            "Supplier credit note history is inconsistent and cannot be reversed.");
    }

    var remaining =
        bill.Total -
        bill.AmountPaid -
        bill.AmountCredited;

    bill.Status =
        remaining <= 0
            ? bill.AmountCredited > 0
                ? BillStatus.Credited
                : BillStatus.Paid
            : bill.AmountPaid > 0 || bill.AmountCredited > 0
                ? BillStatus.PartPaid
                : BillStatus.Posted;

    var reversal =
        new SupplierCreditNoteReversal
        {
            OrganisationId = organisationId,
            SupplierCreditNoteId = credit.Id,
            ReversalDate = reversalDate,
            Reason = reason.Trim(),
            PostedJournalId = journal.Id,
            CreatedByUserId = userId
        };

    db.SupplierCreditNoteReversals.Add(reversal);

    db.AuditEvents.Add(
        new AuditEvent
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = "SupplierCreditNoteReversed",
            EntityType = nameof(SupplierCreditNoteReversal),
            EntityId = reversal.Id.ToString(),
            JsonData =
                JsonSerializer.Serialize(
                    new
                    {
                        credit.CreditNoteNumber,
                        credit.Total,
                        reason,
                        ReversalJournalId = journal.Id,
                        StockMovements = stockReturns.Count
                    })
        });

    await db.SaveChangesAsync(ct);
    await transaction.CommitAsync(ct);

    return reversal;
}
}
