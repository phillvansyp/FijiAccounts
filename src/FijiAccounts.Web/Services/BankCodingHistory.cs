using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public static class BankCodingHistory
{
    public static async Task<HashSet<Guid>> UnmatchableJournalIdsAsync(
        ApplicationDbContext db, Guid organisationId, CancellationToken ct = default)
    {
        var events = await db.AuditEvents.AsNoTracking()
            .Where(x => x.OrganisationId == organisationId &&
                (x.EventType == "BankTransactionCodingReopened" || x.EventType == "BankLedgerRepairJournal"))
            .Select(x => x.JsonData).ToListAsync(ct);
        var ids = new HashSet<Guid>();
        foreach (var value in events)
        {
            using var document = JsonDocument.Parse(value);
            foreach (var name in new[] { "OriginalJournalId", "ReversalJournalId" })
                if (document.RootElement.TryGetProperty(name, out var id) && id.ValueKind == JsonValueKind.String && id.TryGetGuid(out var parsed))
                    ids.Add(parsed);
        }
        var reversals = await db.SupplierPaymentReversals.AsNoTracking()
            .Where(x => x.OrganisationId == organisationId)
            .Select(x => new { Original = x.SupplierPayment.PostedJournalId, Reversal = x.PostedJournalId }).ToListAsync(ct);
        foreach (var reversal in reversals) { ids.Add(reversal.Original); ids.Add(reversal.Reversal); }
        return ids;
    }
}
