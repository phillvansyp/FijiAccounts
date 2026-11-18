using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FijiAccounts.Web.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261119010100_TrackBillDirectoryChanges")]
public sealed class TrackBillDirectoryChanges : Migration
{
    protected override void Up(MigrationBuilder migration)
    {
        // Connection identity and active state are checked by the reader on every lookup.
        static string Bump(string row, string condition = "1") => $"""
            INSERT INTO XeroBillDirectoryVersions(OrganisationId, ConnectionId, Version)
            SELECT {row}.OrganisationId, {row}.XeroConnectionId, 1 WHERE {condition}
            ON CONFLICT(OrganisationId, ConnectionId) DO UPDATE SET Version = Version + 1;
            """;
        migration.Sql($"""
            CREATE TABLE XeroBillDirectoryVersions (
                OrganisationId TEXT NOT NULL, ConnectionId TEXT NOT NULL, Version INTEGER NOT NULL,
                PRIMARY KEY(OrganisationId, ConnectionId)) WITHOUT ROWID;
            INSERT INTO XeroBillDirectoryVersions
            SELECT DISTINCT OrganisationId, XeroConnectionId, 1 FROM XeroMirrorRecords WHERE DatasetKey = 'bills';
            CREATE TRIGGER XeroBillDirectoryInsert AFTER INSERT ON XeroMirrorRecords WHEN NEW.DatasetKey = 'bills'
            BEGIN {Bump("NEW")} END;
            CREATE TRIGGER XeroBillDirectoryDelete AFTER DELETE ON XeroMirrorRecords WHEN OLD.DatasetKey = 'bills'
            BEGIN {Bump("OLD")} END;
            CREATE TRIGGER XeroBillDirectoryUpdate AFTER UPDATE OF Id, OrganisationId, XeroConnectionId,
                DatasetKey, IsArchived, ExternalId, DisplayName, SourceJson ON XeroMirrorRecords
            WHEN OLD.DatasetKey = 'bills' OR NEW.DatasetKey = 'bills'
            BEGIN
                {Bump("OLD", "OLD.DatasetKey = 'bills' AND (NEW.DatasetKey <> 'bills' OR OLD.OrganisationId <> NEW.OrganisationId OR OLD.XeroConnectionId <> NEW.XeroConnectionId)")}
                {Bump("NEW", "NEW.DatasetKey = 'bills'")}
            END;
            """);
    }

    protected override void Down(MigrationBuilder migration) => migration.Sql("""
        DROP TRIGGER XeroBillDirectoryUpdate;
        DROP TRIGGER XeroBillDirectoryDelete;
        DROP TRIGGER XeroBillDirectoryInsert;
        DROP TABLE XeroBillDirectoryVersions;
        """);
}
