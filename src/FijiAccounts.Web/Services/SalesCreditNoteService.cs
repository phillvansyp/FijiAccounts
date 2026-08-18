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
        var controls = await db.LedgerAccounts.Where(x => x.OrganisationId == request.OrganisationId && (x.Code == "1100" || x.Code == "2100")).ToDictionaryAsync(x => x.Code, ct); if (!controls.ContainsKey("1100") || !controls.ContainsKey("2100")) throw new InvalidOperationException("Required receivables and tax control accounts are missing.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var sequence = (await db.SalesCreditNotes.Where(x => x.OrganisationId == request.OrganisationId).MaxAsync(x => (long?)x.SequenceNumber, ct) ?? 0) + 1; var number = $"CN-{sequence:D6}";
        var journalLines = invoice.Lines.GroupBy(x => x.RevenueAccountId).Select(x => new JournalLineInput(x.Key, number, decimal.Round(x.Sum(y => y.NetAmount) * ratio, 2, MidpointRounding.AwayFromZero), 0)).ToList();
        var allocatedNet = journalLines.Sum(x => x.Debit); if (journalLines.Count > 0 && allocatedNet != net) journalLines[0] = journalLines[0] with { Debit = journalLines[0].Debit + net - allocatedNet };
        if (vat > 0) journalLines.Add(new(controls["2100"].Id, number, vat, 0)); journalLines.Add(new(controls["1100"].Id, number, 0, request.Amount));
        var issues = request.RestockTrackedItems ? await db.InventoryMovements.Where(x => x.OrganisationId == request.OrganisationId && x.Reference == invoice.InvoiceNumber && x.QuantityChange < 0).ToListAsync(ct) : [];
        foreach (var issue in issues) { var item = invoice.Lines.Select(x => x.ProductItem).First(x => x?.Id == issue.ProductItemId)!; if (item.InventoryAccountId is null || item.CostAdjustmentAccountId is null) throw new InvalidOperationException($"Inventory accounts are missing for {item.Code}."); var value = decimal.Round(-issue.ValueChange * ratio, 2, MidpointRounding.AwayFromZero); if (value > 0) { journalLines.Add(new(item.InventoryAccountId.Value, $"Return {item.Code}", value, 0)); journalLines.Add(new(item.CostAdjustmentAccountId.Value, $"Reverse cost {item.Code}", 0, value)); } }
        var journal = await posting.PostAsync(userId, new(request.OrganisationId, request.Date, number, $"Credit note for {invoice.InvoiceNumber}: {request.Reason.Trim()}", journalLines), ct);
        var credit = new SalesCreditNote { OrganisationId = request.OrganisationId, SalesInvoiceId = invoice.Id, SequenceNumber = sequence, CreditNoteNumber = number, CreditDate = request.Date, Reason = request.Reason.Trim(), Currency = invoice.Currency, Subtotal = net, VatTotal = vat, Total = request.Amount, PostedJournalId = journal.Id, CreatedByUserId = userId };
        foreach (var issue in issues) { var item = invoice.Lines.Select(x => x.ProductItem).First(x => x?.Id == issue.ProductItemId)!; var quantity = decimal.Round(-issue.QuantityChange * ratio, 4, MidpointRounding.AwayFromZero); var value = decimal.Round(-issue.ValueChange * ratio, 2, MidpointRounding.AwayFromZero); item.QuantityOnHand += quantity; db.InventoryMovements.Add(new InventoryMovement { OrganisationId = request.OrganisationId, ProductItemId = item.Id, MovementDate = request.Date, Type = InventoryMovementType.SalesReturn, QuantityChange = quantity, UnitCost = issue.UnitCost, ValueChange = value, Reference = number, Note = $"Stock returned by credit of {invoice.InvoiceNumber}", PostedJournalId = journal.Id, PostedByUserId = userId }); }
        invoice.AmountCredited += request.Amount; if (invoice.Total - invoice.AmountPaid - invoice.AmountCredited == 0) invoice.Status = InvoiceStatus.Credited;
        db.SalesCreditNotes.Add(credit); db.AuditEvents.Add(new AuditEvent { OrganisationId = request.OrganisationId, UserId = userId, EventType = "SalesCreditNotePosted", EntityType = nameof(SalesCreditNote), EntityId = credit.Id.ToString(), JsonData = JsonSerializer.Serialize(new { credit.CreditNoteNumber, invoice.InvoiceNumber, credit.Total, credit.VatTotal }) }); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return credit;
    }
}
