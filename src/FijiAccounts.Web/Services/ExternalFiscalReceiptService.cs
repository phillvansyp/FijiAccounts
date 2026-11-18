using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record ExternalFiscalReceipt(string Device, string ReceiptNumber, DateOnly IssuedOn, string Note);
public sealed record ExternalFiscalReceiptRevision(long Id, ExternalFiscalReceipt Receipt);

public sealed class ExternalFiscalReceiptService(ApplicationDbContext db, TenantAccessService access)
{
    private const string Event = "ExternalFiscalReceiptRecorded";

    private async Task<Organisation> DemandAsync(string user, Guid org, Guid id, bool refund, CancellationToken ct)
    {
        var company = (await access.FindAsync(user, org))?.Organisation;
        var divisions = await access.GetReportDivisionScopeAsync(user, org, ct);
        var allowed = refund
            ? await db.SalesCreditNotes.AnyAsync(x => x.OrganisationId == org && x.Id == id &&
                x.Status == SalesCreditNoteStatus.Posted && (divisions == null || (x.SalesInvoice.DivisionId.HasValue && divisions.Contains(x.SalesInvoice.DivisionId.Value))), ct)
            : await db.SalesInvoices.AnyAsync(x => x.OrganisationId == org && x.Id == id && x.Status != InvoiceStatus.Draft &&
                (divisions == null || (x.DivisionId.HasValue && divisions.Contains(x.DivisionId.Value))), ct);
        if (company?.CountryCode != "FJ" || !allowed) throw new UnauthorizedAccessException("Access to the posted Fiji document is required.");
        return company;
    }

    public async Task<IReadOnlyList<ExternalFiscalReceiptRevision>> ReadAsync(string user, Guid org, Guid id, bool refund, CancellationToken ct = default)
    {
        await DemandAsync(user, org, id, refund, ct);
        var rows = await db.AuditEvents.AsNoTracking().Where(x => x.OrganisationId == org && x.EventType == Event && x.EntityId == id.ToString())
            .OrderByDescending(x => x.Id).Select(x => new { x.Id, x.JsonData }).ToListAsync(ct);
        return rows.Select(x => new ExternalFiscalReceiptRevision(x.Id, JsonSerializer.Deserialize<ExternalFiscalReceipt>(x.JsonData)!)).ToList();
    }

    public async Task SaveAsync(string user, Guid org, Guid id, bool refund, long lastRevision, ExternalFiscalReceipt receipt, CancellationToken ct = default)
    {
        var company = await DemandAsync(user, org, id, refund, ct);
        if (!await access.CanPostJournalsAsync(user, org)) throw new UnauthorizedAccessException("Posting access is required.");
        receipt = receipt with { Device = receipt.Device.Trim(), ReceiptNumber = receipt.ReceiptNumber.Trim(), Note = receipt.Note.Trim() };
        if (receipt.Device.Length is 0 or > 100 || receipt.ReceiptNumber.Length is 0 or > 120 || receipt.Note.Length > 500 ||
            receipt.IssuedOn == default || receipt.IssuedOn > DateOnly.FromDateTime(OrganisationTime.Convert(DateTimeOffset.UtcNow, company).DateTime))
            throw new InvalidOperationException("Enter the device or provider, external receipt number and an issue date on or before today.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var recordId = id.ToString();
        var current = await db.AuditEvents.AsNoTracking().Where(x => x.OrganisationId == org && x.EventType == Event && x.EntityId == recordId)
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        if ((current?.Id ?? 0) != lastRevision) throw new InvalidOperationException("This record changed. Reopen the document before saving.");
        if (current != null && receipt.Note.Length == 0) throw new InvalidOperationException("Explain the correction to the external receipt reference.");
        var duplicate = await db.Database.SqlQuery<long>($"""
            SELECT a.Id AS Value FROM AuditEvents a
            WHERE a.OrganisationId = {org} AND a.EventType = {Event} AND a.EntityId <> {recordId}
              AND json_extract(a.JsonData, '$.Device') = {receipt.Device} COLLATE NOCASE
              AND json_extract(a.JsonData, '$.ReceiptNumber') = {receipt.ReceiptNumber} COLLATE NOCASE
              AND NOT EXISTS (SELECT 1 FROM AuditEvents newer WHERE newer.OrganisationId = a.OrganisationId
                  AND newer.EventType = a.EventType AND newer.EntityId = a.EntityId AND newer.Id > a.Id)
            """).AnyAsync(ct);
        if (duplicate) throw new InvalidOperationException("That external receipt is already linked to another document.");
        db.AuditEvents.Add(new AuditEvent { OrganisationId = org, UserId = user, EventType = Event, EntityId = recordId,
            EntityType = refund ? nameof(SalesCreditNote) : nameof(SalesInvoice), JsonData = JsonSerializer.Serialize(receipt) });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
