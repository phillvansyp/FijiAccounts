using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FijiAccounts.Web.Services;

internal static class XeroSalesDirectory
{
    private sealed record Revision(Guid ConnectionId, long Version);
    private sealed record Header(Guid Id, string ExternalId, string DisplayName, string HeaderJson);
    private sealed record Snapshot(long Version, IReadOnlyList<XeroSalesInvoiceProjection> Invoices);

    internal static async Task<IReadOnlyList<XeroSalesInvoiceProjection>> ReadAsync(
        ApplicationDbContext db, IMemoryCache cache, Guid organisationId)
    {
        var revisions = await db.Database.SqlQuery<Revision>($"""
            SELECT v.ConnectionId, v.Version FROM XeroSalesDirectoryVersions v
            JOIN XeroConnections c ON c.Id = v.ConnectionId AND c.OrganisationId = v.OrganisationId
            WHERE v.OrganisationId = {organisationId} AND c.IsActive = 1
            """).ToListAsync();
        if (revisions.Count == 0) return [];
        var lists = new List<IReadOnlyList<XeroSalesInvoiceProjection>>(revisions.Count);
        foreach (var revision in revisions)
        {
            var key = (nameof(XeroSalesDirectory), organisationId, revision.ConnectionId,
                System.Globalization.CultureInfo.CurrentCulture.Name);
            if (!cache.TryGetValue<Snapshot>(key, out var saved) || saved!.Version != revision.Version)
            {
                var rows = db.Database.SqlQuery<Header>($"""
                    SELECT Id, ExternalId, DisplayName, Header AS HeaderJson FROM XeroSalesDirectoryRows
                    WHERE OrganisationId = {organisationId} AND ConnectionId = {revision.ConnectionId}
                    """).AsAsyncEnumerable();
                var invoices = new List<XeroSalesInvoiceProjection>();
                await foreach (var row in rows)
                {
                    if (XeroSalesInvoiceProjectionParser.TryParseHeader(row.Id, row.ExternalId, row.DisplayName,
                        row.HeaderJson, out var invoice))
                        invoices.Add(invoice);
                }
                saved = new Snapshot(revision.Version, invoices.OrderByDescending(x => x.InvoiceNumber).ThenBy(x => x.RecordId).ToArray());
                cache.Set(key, saved, TimeSpan.FromMinutes(5));
            }
            lists.Add(saved.Invoices);
        }
        return lists.Count == 1 ? lists[0] : lists.SelectMany(list => list).OrderByDescending(x => x.InvoiceNumber).ThenBy(x => x.RecordId).ToArray();
    }
}
