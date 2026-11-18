using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FijiAccounts.Web.Services;

internal static class XeroBillDirectory
{
    private sealed record Revision(Guid ConnectionId, long Version);
    private sealed record Snapshot(List<Revision> Revisions, IReadOnlyList<XeroSalesInvoiceProjection> Bills);

    internal static async Task<IReadOnlyList<XeroSalesInvoiceProjection>> ReadAsync(
        ApplicationDbContext db, IMemoryCache cache, Guid organisationId)
    {
        var revisions = await db.Database.SqlQuery<Revision>($"""
            SELECT v.ConnectionId, v.Version FROM XeroBillDirectoryVersions v
            JOIN XeroConnections c ON c.Id = v.ConnectionId AND c.OrganisationId = v.OrganisationId
            WHERE v.OrganisationId = {organisationId} AND c.IsActive = 1 ORDER BY v.ConnectionId
            """).ToListAsync();
        if (revisions.Count == 0) return [];

        var key = (nameof(XeroBillDirectory), organisationId, System.Globalization.CultureInfo.CurrentCulture.Name);
        if (cache.TryGetValue<Snapshot>(key, out var saved) && saved!.Revisions.SequenceEqual(revisions))
            return saved.Bills;

        var bills = await XeroSalesInvoiceProjectionParser.ReadCurrentAsync(db, organisationId, supplierBills: true);
        cache.Set(key, new Snapshot(revisions, bills), TimeSpan.FromMinutes(5));
        return bills;
    }
}
