using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261119010000_IndexOpenXeroDocuments")]
public sealed class IndexOpenXeroDocuments : Migration
{
    // Exclude only unambiguously paid records. Escaped names and duplicate Status
    // properties go through the normal .NET parser so legacy JSON keeps its meaning.
    internal const string CandidateFilter = """
        DatasetKey IN ('sales-invoices', 'bills') AND IsArchived = 0 AND
        CASE WHEN json_valid(SourceJson) THEN
            (SourceJson ->> '$.Status') IS NOT 'PAID'
            OR instr(SourceJson, '\u') > 0
            OR (json_remove(SourceJson, '$.Status') -> '$.Status') IS NOT NULL
        ELSE 0 END
        """;

    protected override void Up(MigrationBuilder migration) =>
        migration.Sql($"""
            CREATE INDEX IX_XeroMirror_OpenDocuments
            ON XeroMirrorRecords(OrganisationId, DatasetKey, XeroConnectionId)
            WHERE {CandidateFilter};
            CREATE INDEX IX_XeroMirror_ContactDocuments
            ON XeroMirrorRecords(OrganisationId, XeroConnectionId, {XeroContactDocumentQuery.KeySqlV1})
            WHERE {XeroContactDocumentQuery.DatasetSqlV1};
            """);

    protected override void Down(MigrationBuilder migration) =>
        migration.Sql("DROP INDEX IX_XeroMirror_ContactDocuments; DROP INDEX IX_XeroMirror_OpenDocuments;");
}
