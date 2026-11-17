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
        // Keep directory membership independent of sync timestamps and posting permissions.
        // Project compact document keys once so payments can find their contact through an invoice or credit.
        var ids = await db.Database.SqlQuery<Guid>($"""
            WITH links AS MATERIALIZED (
                SELECT l.TargetEntityId AS Party, l.XeroConnectionId AS Conn, lower(l.ExternalId) AS External,
                    date(coalesce(r.SourceUpdatedAt,r.FirstSeenAt,l.LinkedAt)) AS Since
                FROM XeroMirrorEntityLinks l JOIN XeroConnections c ON c.Id=l.XeroConnectionId
                LEFT JOIN XeroMirrorRecords r ON r.OrganisationId=l.OrganisationId AND r.XeroConnectionId=l.XeroConnectionId
                    AND r.DatasetKey=l.DatasetKey AND r.ExternalId=l.ExternalId
                WHERE l.OrganisationId={org} AND c.OrganisationId={org} AND c.IsActive
                    AND l.DatasetKey='contacts' AND l.TargetEntityType='BusinessParty'
            ), documents AS MATERIALIZED (
                SELECT r.XeroConnectionId AS Conn, lower(r.ExternalId) AS External, r.DatasetKey AS Kind,
                    lower(coalesce(json_extract(r.SourceJson,'$.Contact.ContactID'),json_extract(r.SourceJson,'$.Invoice.Contact.ContactID'),
                        json_extract(r.SourceJson,'$.CreditNote.Contact.ContactID'),json_extract(r.SourceJson,'$.Overpayment.Contact.ContactID'),
                        json_extract(r.SourceJson,'$.Prepayment.Contact.ContactID'))) AS ContactKey,
                    lower(coalesce(json_extract(r.SourceJson,'$.Invoice.InvoiceID'),json_extract(r.SourceJson,'$.CreditNote.CreditNoteID'),
                        json_extract(r.SourceJson,'$.Overpayment.OverpaymentID'),json_extract(r.SourceJson,'$.Prepayment.PrepaymentID'))) AS ParentKey,
                    coalesce(json_extract(r.SourceJson,'$.DateString'),json_extract(r.SourceJson,'$.Date')) AS TradeDate,
                    json_extract(r.SourceJson,'$.FullyPaidOnDate') AS PaidDate
                FROM XeroMirrorRecords r JOIN XeroConnections c ON c.Id=r.XeroConnectionId
                WHERE r.OrganisationId={org} AND c.OrganisationId={org} AND c.IsActive AND NOT r.IsArchived
                    AND r.DatasetKey IN ('sales-invoices','bills','sales-credits','supplier-credits','payments','overpayments','prepayments','bank-transactions')
                    AND json_valid(r.SourceJson)
            ), xeroDates(Conn, ContactKey, Stamp) AS MATERIALIZED (
                SELECT Conn, ContactKey, TradeDate FROM documents WHERE ContactKey IS NOT NULL
                UNION ALL SELECT Conn, ContactKey, PaidDate FROM documents WHERE PaidDate IS NOT NULL
                UNION ALL SELECT p.Conn,d.ContactKey,p.TradeDate FROM documents p JOIN documents d
                    ON d.Conn=p.Conn AND d.External=p.ParentKey AND d.Kind IN ('sales-invoices','bills','sales-credits','supplier-credits','prepayments','overpayments')
                    WHERE p.Kind='payments' AND p.ContactKey IS NULL
            ), latestXero AS MATERIALIZED (
                SELECT Conn,ContactKey,max(CASE WHEN Stamp LIKE '/Date(%' THEN date(CAST(substr(Stamp,7) AS INTEGER)/1000,'unixepoch')
                    ELSE date(substr(Stamp,1,10)) END) AS Used
                FROM xeroDates WHERE ContactKey IS NOT NULL GROUP BY Conn,ContactKey
            ), trades(Party, Used) AS (
                SELECT l.Party,x.Used FROM links l LEFT JOIN latestXero x ON x.Conn=l.Conn AND x.ContactKey=l.External
                UNION ALL SELECT CustomerId,IssueDate FROM SalesInvoices WHERE OrganisationId={org}
                UNION ALL SELECT CustomerId,ReceiptDate FROM CustomerReceipts WHERE OrganisationId={org}
                UNION ALL SELECT CustomerId,QuoteDate FROM SalesQuotes WHERE OrganisationId={org}
                UNION ALL SELECT SupplierId,BillDate FROM SupplierBills WHERE OrganisationId={org}
                UNION ALL SELECT SupplierId,BillDate FROM SupplierBillDrafts WHERE OrganisationId={org}
                UNION ALL SELECT SupplierId,PaymentDate FROM SupplierPayments WHERE OrganisationId={org}
                UNION ALL SELECT SupplierId,PaymentDate FROM SupplierPaymentApprovals WHERE OrganisationId={org}
                UNION ALL SELECT SupplierId,OrderDate FROM PurchaseOrders WHERE OrganisationId={org}
                UNION ALL SELECT SupplierId,RequestDate FROM PurchaseRequisitions WHERE OrganisationId={org}
                UNION ALL SELECT i.CustomerId,c.CreditDate FROM SalesCreditNotes c JOIN SalesInvoices i ON i.Id=c.SalesInvoiceId AND i.OrganisationId=c.OrganisationId WHERE c.OrganisationId={org}
                UNION ALL SELECT coalesce(c.SupplierId,b.SupplierId),c.CreditDate FROM SupplierCreditNotes c LEFT JOIN SupplierBills b ON b.Id=c.SupplierBillId AND b.OrganisationId=c.OrganisationId WHERE c.OrganisationId={org}
                UNION ALL SELECT i.CustomerId,v.VoidDate FROM SalesInvoiceVoids v JOIN SalesInvoices i ON i.Id=v.SalesInvoiceId AND i.OrganisationId=v.OrganisationId WHERE v.OrganisationId={org}
                UNION ALL SELECT b.SupplierId,v.VoidDate FROM SupplierBillVoids v JOIN SupplierBills b ON b.Id=v.SupplierBillId AND b.OrganisationId=v.OrganisationId WHERE v.OrganisationId={org}
                UNION ALL SELECT b.SupplierId,v.ReinstatementDate FROM SupplierBillReinstatements v JOIN SupplierBills b ON b.Id=v.SupplierBillId AND b.OrganisationId=v.OrganisationId WHERE v.OrganisationId={org}
                UNION ALL SELECT r.CustomerId,v.ReversalDate FROM CustomerReceiptReversals v JOIN CustomerReceipts r ON r.Id=v.CustomerReceiptId AND r.OrganisationId=v.OrganisationId WHERE v.OrganisationId={org}
                UNION ALL SELECT p.SupplierId,v.ReversalDate FROM SupplierPaymentReversals v JOIN SupplierPayments p ON p.Id=v.SupplierPaymentId AND p.OrganisationId=v.OrganisationId WHERE v.OrganisationId={org}
                UNION ALL SELECT i.CustomerId,v.ReversalDate FROM SalesCreditNoteReversals v JOIN SalesCreditNotes c ON c.Id=v.SalesCreditNoteId AND c.OrganisationId=v.OrganisationId JOIN SalesInvoices i ON i.Id=c.SalesInvoiceId AND i.OrganisationId=c.OrganisationId WHERE v.OrganisationId={org}
                UNION ALL SELECT coalesce(c.SupplierId,b.SupplierId),v.ReversalDate FROM SupplierCreditNoteReversals v JOIN SupplierCreditNotes c ON c.Id=v.SupplierCreditNoteId AND c.OrganisationId=v.OrganisationId LEFT JOIN SupplierBills b ON b.Id=c.SupplierBillId AND b.OrganisationId=c.OrganisationId WHERE v.OrganisationId={org}
                UNION ALL SELECT CustomerId,StartDate FROM RecurringSalesInvoices WHERE OrganisationId={org}
                UNION ALL SELECT SupplierId,StartDate FROM RecurringSupplierBills WHERE OrganisationId={org}
                UNION ALL SELECT CustomerId,coalesce(CompletedDate,StartDate) FROM Projects WHERE OrganisationId={org}
            ), beginnings(Party, Since) AS (
                SELECT upper(EntityId),date(OccurredAt) FROM AuditEvents WHERE OrganisationId={org} AND EntityType='BusinessParty' AND EventType='BusinessPartyCreated'
                UNION ALL SELECT Party,Since FROM links
                UNION ALL SELECT TargetEntityId,date(ImportedAt) FROM XeroMigrationRecords WHERE OrganisationId={org} AND DatasetKey='contacts' AND TargetEntityType='BusinessParty'
            ), lastTrade AS (SELECT Party,max(Used) AS Used FROM trades GROUP BY Party),
            firstKnown AS (SELECT Party,min(Since) AS Since FROM beginnings GROUP BY Party)
            SELECT p.Id AS Value FROM BusinessParties p
            LEFT JOIN lastTrade t ON t.Party=p.Id LEFT JOIN firstKnown f ON f.Party=p.Id
            WHERE p.OrganisationId={org} AND (NOT p.IsActive OR coalesce(t.Used,f.Since,{organisationStart}) <= {cutoff})
            """).ToListAsync();
        return ids.ToHashSet();
    }
}
