using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

internal static class XeroContactDocumentQuery
{
    // A null key retains the legacy search for malformed or ambiguous source documents.
    // A duplicate Contact or ContactID can resolve differently in SQLite and System.Text.Json.
    internal const string KeySqlV1 = """
        CASE WHEN json_valid(SourceJson) THEN
            CASE WHEN instr(SourceJson, '\u') = 0
                  AND (json_remove(SourceJson, '$.Contact') -> '$.Contact') IS NULL
                  AND (json_remove(SourceJson, '$.Contact.ContactID') -> '$.Contact.ContactID') IS NULL
                 THEN upper(SourceJson ->> '$.Contact.ContactID') END
        END
        """;

    internal const string DatasetSqlV1 = """
        IsArchived = 0 AND DatasetKey IN
        ('sales-invoices','bills','sales-credits','supplier-credits','prepayments','overpayments')
        """;

    internal static IAsyncEnumerable<XeroMirrorRecord> Read(
        ApplicationDbContext db, Guid organisationId, Guid connectionId, string externalId)
    {
        const string query = "SELECT * FROM XeroMirrorRecords WHERE OrganisationId = {0} AND XeroConnectionId = {1} AND "
            + DatasetSqlV1 + " AND ";
        // Separate the normal indexed lookup from the uncommon fallback so both use the contact index.
        return db.XeroMirrorRecords.FromSqlRaw(
            query + KeySqlV1 + " = {2} UNION ALL " + query + KeySqlV1
            + " IS NULL AND instr(upper(SourceJson), {2}) > 0",
            organisationId, connectionId, externalId.ToUpperInvariant()).AsNoTracking().AsAsyncEnumerable();
    }
}
