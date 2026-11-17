using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FijiAccounts.Web.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261118010000_CorrectXeroContactRoles")]
public sealed class CorrectXeroContactRoles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Earlier imports gave both roles to contacts without either Xero trading flag.
        // Repair only linked dual-role parties and retain roles supported by trading history.
        migrationBuilder.Sql("""
            WITH linked AS MATERIALIZED (
                SELECT l.OrganisationId, l.TargetEntityId AS PartyId, l.XeroConnectionId,
                       l.ExternalId, r.SourceJson
                FROM XeroMirrorEntityLinks l
                JOIN XeroConnections c ON c.Id = l.XeroConnectionId AND c.OrganisationId = l.OrganisationId AND c.IsActive
                JOIN XeroMirrorRecords r ON r.OrganisationId = l.OrganisationId AND r.XeroConnectionId = l.XeroConnectionId
                    AND r.DatasetKey = l.DatasetKey AND r.ExternalId = l.ExternalId AND NOT r.IsArchived
                JOIN BusinessParties p ON p.Id = l.TargetEntityId AND p.OrganisationId = l.OrganisationId
                WHERE l.DatasetKey = 'contacts' AND l.TargetEntityType = 'BusinessParty'
                    AND p.Type = 3 AND json_valid(r.SourceJson)
            ), evidence(OrganisationId, PartyId, Customer, Supplier) AS (
                SELECT OrganisationId, PartyId,
                       lower(coalesce(json_extract(SourceJson, '$.IsCustomer'), 0)) IN ('1','true'),
                       lower(coalesce(json_extract(SourceJson, '$.IsSupplier'), 0)) IN ('1','true')
                FROM linked
                UNION ALL
                SELECT l.OrganisationId, l.PartyId,
                       d.DatasetKey IN ('sales-invoices','sales-credits') OR
                           (d.DatasetKey IN ('prepayments','overpayments') AND json_extract(d.SourceJson,'$.Type') LIKE 'RECEIVE%'),
                       d.DatasetKey IN ('bills','supplier-credits') OR
                           (d.DatasetKey IN ('prepayments','overpayments') AND json_extract(d.SourceJson,'$.Type') LIKE 'SPEND%')
                FROM XeroMirrorRecords d
                JOIN linked l ON l.OrganisationId = d.OrganisationId AND l.XeroConnectionId = d.XeroConnectionId
                    AND l.ExternalId = CASE WHEN json_valid(d.SourceJson) THEN json_extract(d.SourceJson,'$.Contact.ContactID') END COLLATE NOCASE
                WHERE NOT d.IsArchived AND d.DatasetKey IN ('sales-invoices','bills','sales-credits','supplier-credits','prepayments','overpayments')
                UNION ALL SELECT OrganisationId, CustomerId, 1, 0 FROM SalesInvoices
                UNION ALL SELECT OrganisationId, SupplierId, 0, 1 FROM SupplierBills
                UNION ALL SELECT OrganisationId, SupplierId, 0, 1 FROM SupplierBillDrafts
                UNION ALL SELECT OrganisationId, SupplierId, 0, 1 FROM SupplierCreditNotes
            ), roles AS MATERIALIZED (
                SELECT OrganisationId, PartyId, max(Customer) + 2 * max(Supplier) AS Role
                FROM evidence GROUP BY OrganisationId, PartyId
            )
            UPDATE BusinessParties AS p SET Type = roles.Role
            FROM roles
            WHERE p.OrganisationId = roles.OrganisationId AND p.Id = roles.PartyId AND p.Type = 3 AND roles.Role <> 3
                AND EXISTS (SELECT 1 FROM linked l WHERE l.OrganisationId = p.OrganisationId AND l.PartyId = p.Id);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // A rollback cannot distinguish the old fallback from later legitimate role changes.
    }
}
