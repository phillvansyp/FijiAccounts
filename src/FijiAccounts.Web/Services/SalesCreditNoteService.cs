using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record SalesCreditNoteRequest(Guid OrganisationId, Guid SalesInvoiceId, DateOnly Date, string Reason, decimal Amount, bool RestockTrackedItems);

public sealed class SalesCreditNoteService(ApplicationDbContext db, TenantAccessService access, JournalPostingService posting)
{
    public async Task<SalesCreditNote> CreateAsync(string userId, SalesCreditNoteRequest request, CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, request.OrganisationId)) throw new UnauthorizedAccessException("You cannot issue credit notes for this organisation.");
        var invoice = await db.SalesInvoices.Include(x => x.Lines).ThenInclude(x => x.ProductItem).SingleOrDefaultAsync(x => x.Id == request.SalesInvoiceId && x.OrganisationId == request.OrganisationId, ct) ?? throw new InvalidOperationException("Invoice not found.");
        if (invoice.Status is InvoiceStatus.Draft or InvoiceStatus.Voided or InvoiceStatus.Credited) throw new InvalidOperationException("This invoice cannot be credited.");
        var available = invoice.Total - invoice.AmountPaid - invoice.AmountCredited; if (request.Amount <= 0 || request.Amount > available) throw new InvalidOperationException($"Credit must be between $0.01 and ${available:N2}.");
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException("Enter a reason for the credit note.");
        var ratio = request.Amount / invoice.Total; var net = decimal.Round(invoice.Subtotal * ratio, 2, MidpointRounding.AwayFromZero); var vat = request.Amount - net;
        var controls = await db.LedgerAccounts
    .Where(x =>
        x.OrganisationId == request.OrganisationId &&
        x.IsActive &&
        (x.Code == "1100" || x.Code == "2100"))
    .ToDictionaryAsync(
        x => x.Code,
        ct);

if (!controls.TryGetValue(
        "1100",
        out var receivables) ||
    receivables.Type != AccountType.Asset)
{
    throw new InvalidOperationException(
        "Accounts Receivable (1100) must be an active Asset account.");
}

if (!controls.TryGetValue(
        "2100",
        out var vatPayable) ||
    vatPayable.Type != AccountType.Liability)
{
    throw new InvalidOperationException(
        "VAT Payable (2100) must be an active Liability account.");
}
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var sequence = (await db.SalesCreditNotes.Where(x => x.OrganisationId == request.OrganisationId).MaxAsync(x => (long?)x.SequenceNumber, ct) ?? 0) + 1; var number = $"CN-{sequence:D6}";
        var journalLines = invoice.Lines.GroupBy(x => x.RevenueAccountId).Select(x => new JournalLineInput(x.Key, number, decimal.Round(x.Sum(y => y.NetAmount) * ratio, 2, MidpointRounding.AwayFromZero), 0)).ToList();
        var allocatedNet = journalLines.Sum(x => x.Debit); if (journalLines.Count > 0 && allocatedNet != net) journalLines[0] = journalLines[0] with { Debit = journalLines[0].Debit + net - allocatedNet };
        if (vat > 0)
{
    journalLines.Add(
        new(vatPayable.Id, number, vat, 0));
}

journalLines.Add(
    new(receivables.Id, number, 0, request.Amount));
        var issues = request.RestockTrackedItems ? await db.InventoryMovements.Where(x => x.OrganisationId == request.OrganisationId && x.Reference == invoice.InvoiceNumber && x.QuantityChange < 0).ToListAsync(ct) : [];
        foreach (var issue in issues) { var item = invoice.Lines.Select(x => x.ProductItem).First(x => x?.Id == issue.ProductItemId)!; if (item.InventoryAccountId is null || item.CostAdjustmentAccountId is null) throw new InvalidOperationException($"Inventory accounts are missing for {item.Code}."); var value = decimal.Round(-issue.ValueChange * ratio, 2, MidpointRounding.AwayFromZero); if (value > 0) { journalLines.Add(new(item.InventoryAccountId.Value, $"Return {item.Code}", value, 0)); journalLines.Add(new(item.CostAdjustmentAccountId.Value, $"Reverse cost {item.Code}", 0, value)); } }
        var journal = await posting.PostAsync(userId, new(request.OrganisationId, request.Date, number, $"Credit note for {invoice.InvoiceNumber}: {request.Reason.Trim()}", journalLines), ct);
        var credit = new SalesCreditNote { OrganisationId = request.OrganisationId, SalesInvoiceId = invoice.Id, SequenceNumber = sequence, CreditNoteNumber = number, CreditDate = request.Date, Reason = request.Reason.Trim(), Currency = invoice.Currency, Subtotal = net, VatTotal = vat, Total = request.Amount, PostedJournalId = journal.Id, CreatedByUserId = userId };
        foreach (var issue in issues) { var item = invoice.Lines.Select(x => x.ProductItem).First(x => x?.Id == issue.ProductItemId)!; var quantity = decimal.Round(-issue.QuantityChange * ratio, 4, MidpointRounding.AwayFromZero); var value = decimal.Round(-issue.ValueChange * ratio, 2, MidpointRounding.AwayFromZero); item.QuantityOnHand += quantity; db.InventoryMovements.Add(new InventoryMovement { OrganisationId = request.OrganisationId, ProductItemId = item.Id, MovementDate = request.Date, Type = InventoryMovementType.SalesReturn, QuantityChange = quantity, UnitCost = issue.UnitCost, ValueChange = value, Reference = number, Note = $"Stock returned by credit of {invoice.InvoiceNumber}", PostedJournalId = journal.Id, PostedByUserId = userId }); }
        invoice.AmountCredited += request.Amount;

var remaining =
    invoice.Total -
    invoice.AmountPaid -
    invoice.AmountCredited;

invoice.Status =
    remaining <= 0
        ? InvoiceStatus.Credited
        : invoice.AmountPaid > 0 || invoice.AmountCredited > 0
            ? InvoiceStatus.PartPaid
            : InvoiceStatus.Posted;
        db.SalesCreditNotes.Add(credit); db.AuditEvents.Add(new AuditEvent { OrganisationId = request.OrganisationId, UserId = userId, EventType = "SalesCreditNotePosted", EntityType = nameof(SalesCreditNote), EntityId = credit.Id.ToString(), JsonData = JsonSerializer.Serialize(new { credit.CreditNoteNumber, invoice.InvoiceNumber, credit.Total, credit.VatTotal }) }); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return credit;
    }

    public async Task<SalesCreditNoteReversal> ReverseAsync(
    string userId,
    Guid organisationId,
    Guid creditNoteId,
    DateOnly reversalDate,
    string reason,
    CancellationToken ct = default)
{
    if (!await access.CanPostJournalsAsync(userId, organisationId))
        throw new UnauthorizedAccessException(
            "You cannot reverse credit notes for this organisation.");

    if (string.IsNullOrWhiteSpace(reason))
        throw new InvalidOperationException(
            "Enter a reason for reversing the credit note.");

    var credit =
        await db.SalesCreditNotes
            .Include(x => x.SalesInvoice)
                .ThenInclude(x => x.Lines)
                    .ThenInclude(x => x.ProductItem)
            .SingleOrDefaultAsync(
                x =>
                    x.Id == creditNoteId &&
                    x.OrganisationId == organisationId,
                ct)
        ?? throw new InvalidOperationException(
            "Sales credit note not found.");

    if (await db.SalesCreditNoteReversals.AnyAsync(
            x => x.SalesCreditNoteId == creditNoteId,
            ct))
    {
        throw new InvalidOperationException(
            "This sales credit note has already been reversed.");
    }

    var stockReturns =
        await db.InventoryMovements
            .Include(x => x.ProductItem)
            .Where(
                x =>
                    x.OrganisationId == organisationId &&
                    x.PostedJournalId == credit.PostedJournalId &&
                    x.Reference == credit.CreditNoteNumber &&
                    x.Type == InventoryMovementType.SalesReturn)
            .ToListAsync(ct);

    foreach (var movement in stockReturns)
    {
        if (movement.ProductItem.QuantityOnHand < movement.QuantityChange)
        {
            throw new InvalidOperationException(
                $"Cannot reverse this sales credit note because " +
                $"{movement.ProductItem.Code} no longer has all returned units on hand.");
        }
    }

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
                $"Reverse sales credit note {credit.CreditNoteNumber}: {reason.Trim()}",
                lines),
            ct);

    foreach (var movement in stockReturns)
    {
        var item = movement.ProductItem;

        item.QuantityOnHand -= movement.QuantityChange;

        db.InventoryMovements.Add(
            new InventoryMovement
            {
                OrganisationId = organisationId,
                ProductItemId = item.Id,
                MovementDate = reversalDate,
                Type = InventoryMovementType.AdjustmentDecrease,
                QuantityChange = -movement.QuantityChange,
                UnitCost = movement.UnitCost,
                ValueChange = -movement.ValueChange,
                Reference = reference,
                Note =
                    $"Stock removed by reversal of {credit.CreditNoteNumber}",
                PostedJournalId = journal.Id,
                PostedByUserId = userId
            });
    }

    var invoice =
        credit.SalesInvoice;

    invoice.AmountCredited -= credit.Total;

    if (invoice.AmountCredited < 0)
    {
        throw new InvalidOperationException(
            "Credit note history is inconsistent and cannot be reversed.");
    }

    var remaining =
        invoice.Total -
        invoice.AmountPaid -
        invoice.AmountCredited;

    invoice.Status =
        remaining <= 0
            ? invoice.AmountCredited > 0
                ? InvoiceStatus.Credited
                : InvoiceStatus.Paid
            : invoice.AmountPaid > 0 || invoice.AmountCredited > 0
                ? InvoiceStatus.PartPaid
                : InvoiceStatus.Posted;

    var reversal =
        new SalesCreditNoteReversal
        {
            OrganisationId = organisationId,
            SalesCreditNoteId = credit.Id,
            ReversalDate = reversalDate,
            Reason = reason.Trim(),
            PostedJournalId = journal.Id,
            CreatedByUserId = userId
        };

    db.SalesCreditNoteReversals.Add(reversal);

    db.AuditEvents.Add(
        new AuditEvent
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = "SalesCreditNoteReversed",
            EntityType = nameof(SalesCreditNoteReversal),
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
