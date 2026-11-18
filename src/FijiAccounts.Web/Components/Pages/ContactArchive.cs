using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Components.Pages;

internal static class ContactArchive
{
    public static async Task<HashSet<Guid>> FindArchivedAsync(ApplicationDbContext db, Organisation organisation, DateOnly today)
    {
        var org = organisation.Id;
        var cutoff = today.AddMonths(-24);
        var organisationStart = DateOnly.FromDateTime(OrganisationTime.Convert(organisation.CreatedAt, organisation).Date);

        // The stored maxima follow document writes. Resolve only contact links here so switching a
        // connection or relinking a Xero contact takes effect immediately without replaying its history.
        var ids = await db.Database.SqlQuery<Guid>($"""
            WITH xero AS (
                SELECT l.TargetEntityId Party,max(x.Used) Used,min(date(coalesce(r.SourceUpdatedAt,r.FirstSeenAt,l.LinkedAt))) Since
                FROM XeroMirrorEntityLinks l
                JOIN XeroConnections c ON c.Id=l.XeroConnectionId AND c.OrganisationId=l.OrganisationId AND c.IsActive
                LEFT JOIN DirectoryXeroLast x ON x.Org=l.OrganisationId AND x.Conn=l.XeroConnectionId AND x.ContactKey=lower(l.ExternalId)
                LEFT JOIN XeroMirrorRecords r ON r.XeroConnectionId=l.XeroConnectionId AND r.OrganisationId=l.OrganisationId
                    AND r.DatasetKey='contacts' AND r.ExternalId=l.ExternalId
                WHERE l.OrganisationId={org} AND l.DatasetKey='contacts' AND l.TargetEntityType='BusinessParty'
                GROUP BY l.TargetEntityId
            ), beginnings AS (
                SELECT Party,min(Since) Since FROM DirectoryBeginnings WHERE Org={org} GROUP BY Party
            )
            SELECT p.Id AS Value FROM BusinessParties p
            LEFT JOIN DirectoryLocalLast d ON d.Org=p.OrganisationId AND d.Party=p.Id
            LEFT JOIN xero x ON x.Party=p.Id LEFT JOIN beginnings b ON b.Party=p.Id
            WHERE p.OrganisationId={org} AND (NOT p.IsActive OR coalesce(
                nullif(max(coalesce(d.Used,''),coalesce(x.Used,'')),''),
                min(coalesce(x.Since,b.Since),coalesce(b.Since,x.Since)),{organisationStart}) <= {cutoff})
            """).ToListAsync();
        return ids.ToHashSet();
    }
}
