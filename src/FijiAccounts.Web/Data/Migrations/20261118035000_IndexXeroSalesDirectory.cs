using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FijiAccounts.Web.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261118035000_IndexXeroSalesDirectory")]
public sealed class IndexXeroSalesDirectory : Migration
{
    protected override void Up(MigrationBuilder migration)
    {
        // Keep JSON scalar types and decimal literals intact for the same parser used by invoice detail.
        const string fields = """
            'InvoiceNumber', SourceJson -> '$.InvoiceNumber', 'Reference', SourceJson -> '$.Reference',
            'Contact', json_object('Name', SourceJson -> '$.Contact.Name', 'ContactID', SourceJson -> '$.Contact.ContactID'),
            'Date', SourceJson -> '$.Date', 'DueDate', SourceJson -> '$.DueDate', 'Status', SourceJson -> '$.Status',
            'CurrencyCode', SourceJson -> '$.CurrencyCode', 'CurrencyRate', SourceJson -> '$.CurrencyRate',
            'SubTotal', SourceJson -> '$.SubTotal', 'TotalTax', SourceJson -> '$.TotalTax', 'Total', SourceJson -> '$.Total',
            'AmountDue', SourceJson -> '$.AmountDue', 'AmountPaid', SourceJson -> '$.AmountPaid',
            'AmountCredited', SourceJson -> '$.AmountCredited'
            """;

        migration.Sql($"""
            CREATE TABLE XeroSalesDirectoryRows (
                Id TEXT PRIMARY KEY, OrganisationId TEXT NOT NULL, ConnectionId TEXT NOT NULL,
                ExternalId TEXT NOT NULL, DisplayName TEXT NOT NULL, Header TEXT NOT NULL);
            CREATE INDEX IX_XeroSalesDirectory_Connection ON XeroSalesDirectoryRows(OrganisationId, ConnectionId);
            CREATE TABLE XeroSalesDirectoryVersions (
                OrganisationId TEXT NOT NULL, ConnectionId TEXT NOT NULL, Version INTEGER NOT NULL,
                PRIMARY KEY(OrganisationId, ConnectionId)) WITHOUT ROWID;

            CREATE VIEW XeroSalesDirectorySource AS
                SELECT Id, OrganisationId, XeroConnectionId AS ConnectionId, ExternalId, DisplayName,
                    json_object({fields}) AS Header
                FROM XeroMirrorRecords
                WHERE DatasetKey = 'sales-invoices' AND IsArchived = 0 AND json_valid(SourceJson);
            INSERT INTO XeroSalesDirectoryRows SELECT * FROM XeroSalesDirectorySource;
            INSERT INTO XeroSalesDirectoryVersions SELECT DISTINCT OrganisationId, ConnectionId, 1 FROM XeroSalesDirectoryRows;

            CREATE TRIGGER XeroSalesDirectoryInsert AFTER INSERT ON XeroMirrorRecords
            WHEN NEW.DatasetKey = 'sales-invoices' AND NEW.IsArchived = 0
            BEGIN
                INSERT INTO XeroSalesDirectoryRows SELECT * FROM XeroSalesDirectorySource WHERE Id = NEW.Id;
                {Bump("NEW")}
            END;

            CREATE TRIGGER XeroSalesDirectoryUpdate AFTER UPDATE OF
                Id, OrganisationId, XeroConnectionId, DatasetKey, IsArchived, ExternalId, DisplayName, SourceJson ON XeroMirrorRecords
            WHEN OLD.DatasetKey = 'sales-invoices' OR NEW.DatasetKey = 'sales-invoices'
            BEGIN
                DELETE FROM XeroSalesDirectoryRows WHERE Id = OLD.Id;
                INSERT INTO XeroSalesDirectoryRows SELECT * FROM XeroSalesDirectorySource WHERE Id = NEW.Id;
                INSERT INTO XeroSalesDirectoryVersions(OrganisationId, ConnectionId, Version)
                SELECT OLD.OrganisationId, OLD.XeroConnectionId, 1 WHERE OLD.DatasetKey = 'sales-invoices'
                UNION SELECT NEW.OrganisationId, NEW.XeroConnectionId, 1 WHERE NEW.DatasetKey = 'sales-invoices'
                ON CONFLICT(OrganisationId, ConnectionId) DO UPDATE SET Version = Version + 1;
            END;

            CREATE TRIGGER XeroSalesDirectoryDelete AFTER DELETE ON XeroMirrorRecords
            WHEN OLD.DatasetKey = 'sales-invoices'
            BEGIN
                DELETE FROM XeroSalesDirectoryRows WHERE Id = OLD.Id;
                {Bump("OLD")}
            END;
            """);
    }

    private static string Bump(string row) => $"""
        INSERT INTO XeroSalesDirectoryVersions(OrganisationId, ConnectionId, Version)
        VALUES ({row}.OrganisationId, {row}.XeroConnectionId, 1)
        ON CONFLICT(OrganisationId, ConnectionId) DO UPDATE SET Version = Version + 1;
        """;

    protected override void Down(MigrationBuilder migration) => migration.Sql("""
        DROP TRIGGER XeroSalesDirectoryDelete;
        DROP TRIGGER XeroSalesDirectoryUpdate;
        DROP TRIGGER XeroSalesDirectoryInsert;
        DROP VIEW XeroSalesDirectorySource;
        DROP TABLE XeroSalesDirectoryVersions;
        DROP TABLE XeroSalesDirectoryRows;
        """);
}
