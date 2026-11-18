using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FijiAccounts.Web.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261118024500_StoreContactActivity")]
public sealed class StoreContactActivity : Migration
{
    private const string Parents = "'sales-invoices','bills','sales-credits','supplier-credits','overpayments','prepayments'";
    private const string Financial = Parents + ",'payments','bank-transactions'";

    protected override void Up(MigrationBuilder m)
    {
        CreateLocalActivity(m);
        CreateContactBeginnings(m);
        CreateXeroActivity(m);
    }

    // Party expressions are used with OLD and NEW. Parent changes also refresh both
    // customers/suppliers, which accounts for every dependent credit, void and reversal.
    private static readonly (string Table, string Party, string Columns)[] LocalSources =
    [
        ("SalesInvoices", "r.CustomerId", "CustomerId,IssueDate"),
        ("CustomerReceipts", "r.CustomerId", "CustomerId,ReceiptDate"),
        ("SalesQuotes", "r.CustomerId", "CustomerId,QuoteDate"),
        ("SupplierBills", "r.SupplierId", "SupplierId,BillDate"),
        ("SupplierBillDrafts", "r.SupplierId", "SupplierId,BillDate"),
        ("SupplierPayments", "r.SupplierId", "SupplierId,PaymentDate"),
        ("SupplierPaymentApprovals", "r.SupplierId", "SupplierId,PaymentDate"),
        ("PurchaseOrders", "r.SupplierId", "SupplierId,OrderDate"),
        ("PurchaseRequisitions", "r.SupplierId", "SupplierId,RequestDate"),
        ("SalesCreditNotes", InvoiceParty, "SalesInvoiceId,CreditDate"),
        ("SupplierCreditNotes", "coalesce(r.SupplierId," + BillParty + ")", "SupplierId,SupplierBillId,CreditDate"),
        ("SalesInvoiceVoids", InvoiceParty, "SalesInvoiceId,VoidDate"),
        ("SupplierBillVoids", BillParty, "SupplierBillId,VoidDate"),
        ("SupplierBillReinstatements", BillParty, "SupplierBillId,ReinstatementDate"),
        ("CustomerReceiptReversals", "(SELECT CustomerId FROM CustomerReceipts WHERE Id=r.CustomerReceiptId AND OrganisationId=r.OrganisationId)", "CustomerReceiptId,ReversalDate"),
        ("SupplierPaymentReversals", "(SELECT SupplierId FROM SupplierPayments WHERE Id=r.SupplierPaymentId AND OrganisationId=r.OrganisationId)", "SupplierPaymentId,ReversalDate"),
        ("SalesCreditNoteReversals", "(SELECT i.CustomerId FROM SalesCreditNotes c JOIN SalesInvoices i ON i.Id=c.SalesInvoiceId AND i.OrganisationId=c.OrganisationId WHERE c.Id=r.SalesCreditNoteId AND c.OrganisationId=r.OrganisationId)", "SalesCreditNoteId,ReversalDate"),
        ("SupplierCreditNoteReversals", "(SELECT coalesce(c.SupplierId,b.SupplierId) FROM SupplierCreditNotes c LEFT JOIN SupplierBills b ON b.Id=c.SupplierBillId AND b.OrganisationId=c.OrganisationId WHERE c.Id=r.SupplierCreditNoteId AND c.OrganisationId=r.OrganisationId)", "SupplierCreditNoteId,ReversalDate"),
        ("RecurringSalesInvoices", "r.CustomerId", "CustomerId,StartDate"),
        ("RecurringSupplierBills", "r.SupplierId", "SupplierId,StartDate"),
        ("Projects", "r.CustomerId", "CustomerId,StartDate,CompletedDate")
    ];
    private const string InvoiceParty = "(SELECT CustomerId FROM SalesInvoices WHERE Id=r.SalesInvoiceId AND OrganisationId=r.OrganisationId)";
    private const string BillParty = "(SELECT SupplierId FROM SupplierBills WHERE Id=r.SupplierBillId AND OrganisationId=r.OrganisationId)";

    private static void CreateLocalActivity(MigrationBuilder m)
    {
        var sources = LocalSources.Select(source =>
        {
            var date = source.Table == "Projects" ? "coalesce(r.CompletedDate,r.StartDate)" : "r." + source.Columns.Split(',')[^1];
            return $"SELECT r.OrganisationId,{source.Party},{date} FROM {source.Table} r";
        });
        m.Sql("CREATE VIEW DirectoryLocalTrades(Org,Party,Used) AS " + string.Join(" UNION ALL ", sources));
        m.Sql("""
            CREATE TABLE DirectoryLocalLast(Org TEXT,Party TEXT,Used TEXT NOT NULL,PRIMARY KEY(Org,Party)) WITHOUT ROWID;
            INSERT INTO DirectoryLocalLast SELECT Org,Party,max(Used) FROM DirectoryLocalTrades
            WHERE Party IS NOT NULL AND Used IS NOT NULL GROUP BY Org,Party;
            """);
        foreach (var source in LocalSources)
        {
            string Refresh(string row)
            {
                var party=source.Party.Replace("r.",row+".");
                return $"""
                    DELETE FROM DirectoryLocalLast WHERE Org={row}.OrganisationId AND Party={party};
                    INSERT INTO DirectoryLocalLast SELECT Org,Party,max(Used) FROM DirectoryLocalTrades
                    WHERE Org={row}.OrganisationId AND Party={party} AND Used IS NOT NULL GROUP BY Org,Party;
                    """;
            }
            Triggers(m, source.Table, "OrganisationId," + source.Columns, Refresh("OLD"), Refresh("NEW"));
        }
    }

    // An empty contact's age is based on its first known date, not the latest sync.
    private static void CreateContactBeginnings(MigrationBuilder m)
    {
        m.Sql("""
            CREATE TABLE DirectoryBeginnings(Source TEXT,Id TEXT,Org TEXT NOT NULL,Party TEXT NOT NULL,Since TEXT NOT NULL,
                PRIMARY KEY(Source,Id)) WITHOUT ROWID;
            CREATE INDEX DirectoryBeginningsParty ON DirectoryBeginnings(Org,Party,Since);
            """);
        void Source(string table,string columns,string filter,string party,string date)
        {
            var select=$"SELECT '{table}',r.Id,r.OrganisationId,{party},date({date}) FROM {table} r WHERE {filter} AND {party} IS NOT NULL AND date({date}) IS NOT NULL";
            m.Sql($"INSERT INTO DirectoryBeginnings {select}");
            Triggers(m,table,columns,$"DELETE FROM DirectoryBeginnings WHERE Source='{table}' AND Id=OLD.Id;",
                $"INSERT INTO DirectoryBeginnings {select} AND r.Id=NEW.Id;");
        }
        Source("AuditEvents","OrganisationId,EntityType,EntityId,EventType,OccurredAt","r.EntityType='BusinessParty' AND r.EventType='BusinessPartyCreated'","upper(r.EntityId)","r.OccurredAt");
        Source("XeroMigrationRecords","OrganisationId,DatasetKey,TargetEntityType,TargetEntityId,ImportedAt","r.DatasetKey='contacts' AND r.TargetEntityType='BusinessParty'","r.TargetEntityId","r.ImportedAt");
    }

    private static void Triggers(MigrationBuilder m,string table,string columns,string remove,string add,string when="")
    {
        m.Sql($"CREATE TRIGGER Directory_{table}_i AFTER INSERT ON {table} BEGIN {add} END;");
        m.Sql($"CREATE TRIGGER Directory_{table}_d AFTER DELETE ON {table} BEGIN {remove} END;");
        m.Sql($"CREATE TRIGGER Directory_{table}_u AFTER UPDATE OF {columns} ON {table} {when} BEGIN {remove} {add} END;");
    }

    private static string XeroDate(string field) =>
        $"CASE WHEN {field} LIKE '/Date(%' THEN date(CAST(substr({field},7) AS INTEGER)/1000,'unixepoch') ELSE date(substr({field},1,10)) END";

    private static void CreateXeroActivity(MigrationBuilder m)
    {
        m.Sql($"""
            CREATE INDEX DirectoryXeroContactDates ON XeroMirrorRecords(OrganisationId,XeroConnectionId,DatasetKey,ExternalId,SourceUpdatedAt,FirstSeenAt) WHERE DatasetKey='contacts';
            CREATE TABLE DirectoryXeroDocs(Id TEXT PRIMARY KEY,Org TEXT NOT NULL,Conn TEXT NOT NULL,Kind TEXT NOT NULL,
                External TEXT NOT NULL,DirectKey TEXT,ParentKey TEXT,ContactKey TEXT,Used TEXT);
            CREATE INDEX DirectoryXeroParent ON DirectoryXeroDocs(Org,Conn,External) WHERE Kind IN ({Parents});
            CREATE INDEX DirectoryXeroChildren ON DirectoryXeroDocs(Org,Conn,ParentKey) WHERE Kind='payments' AND DirectKey IS NULL;
            CREATE INDEX DirectoryXeroNewest ON DirectoryXeroDocs(Org,Conn,ContactKey,Used);
            CREATE TABLE DirectoryXeroLast(Org TEXT,Conn TEXT,ContactKey TEXT,Used TEXT NOT NULL,PRIMARY KEY(Org,Conn,ContactKey)) WITHOUT ROWID;
            CREATE VIEW DirectoryXeroSource AS
            SELECT Id,Org,Conn,Kind,External,DirectKey,ParentKey,
                   nullif(max(coalesce({XeroDate("TradeDate")},''),coalesce({XeroDate("PaidDate")},'')),'') AS Used FROM (
                SELECT Id,OrganisationId Org,XeroConnectionId Conn,DatasetKey Kind,lower(ExternalId) External,
                    lower(coalesce(json_extract(SourceJson,'$.Contact.ContactID'),json_extract(SourceJson,'$.Invoice.Contact.ContactID'),
                        json_extract(SourceJson,'$.CreditNote.Contact.ContactID'),json_extract(SourceJson,'$.Overpayment.Contact.ContactID'),
                        json_extract(SourceJson,'$.Prepayment.Contact.ContactID'))) AS DirectKey,
                    lower(coalesce(json_extract(SourceJson,'$.Invoice.InvoiceID'),json_extract(SourceJson,'$.CreditNote.CreditNoteID'),
                        json_extract(SourceJson,'$.Overpayment.OverpaymentID'),json_extract(SourceJson,'$.Prepayment.PrepaymentID'))) AS ParentKey,
                    coalesce(json_extract(SourceJson,'$.DateString'),json_extract(SourceJson,'$.Date')) AS TradeDate,
                    json_extract(SourceJson,'$.FullyPaidOnDate') AS PaidDate
                FROM XeroMirrorRecords WHERE NOT IsArchived AND DatasetKey IN ({Financial}) AND json_valid(SourceJson)
            );
            """);
        string Resolve(string row) => $"""
            (SELECT p.DirectKey FROM DirectoryXeroDocs p WHERE p.Org={row}.Org AND p.Conn={row}.Conn
             AND p.External={row}.ParentKey AND p.Kind IN ({Parents}) AND p.DirectKey IS NOT NULL LIMIT 1)
            """;
        m.Sql($"""
            INSERT INTO DirectoryXeroDocs SELECT Id,Org,Conn,Kind,External,DirectKey,ParentKey,DirectKey,Used FROM DirectoryXeroSource;
            UPDATE DirectoryXeroDocs SET ContactKey={Resolve("DirectoryXeroDocs")}
                WHERE Kind='payments' AND DirectKey IS NULL AND ParentKey IS NOT NULL;
            INSERT INTO DirectoryXeroLast SELECT Org,Conn,ContactKey,max(Used) FROM DirectoryXeroDocs
                WHERE ContactKey IS NOT NULL AND Used IS NOT NULL GROUP BY Org,Conn,ContactKey;
            """);
        string Latest(string row) => $"""
            DELETE FROM DirectoryXeroLast WHERE Org={row}.Org AND Conn={row}.Conn AND ContactKey={row}.ContactKey;
            INSERT INTO DirectoryXeroLast SELECT Org,Conn,ContactKey,Used FROM DirectoryXeroDocs
                WHERE Org={row}.Org AND Conn={row}.Conn AND ContactKey={row}.ContactKey AND Used IS NOT NULL
                ORDER BY Used DESC LIMIT 1;
            """;
        string Relink(string row) => $"""
            UPDATE DirectoryXeroDocs SET ContactKey={Resolve("DirectoryXeroDocs")}
                WHERE {row}.Kind IN ({Parents}) AND Org={row}.Org AND Conn={row}.Conn AND ParentKey={row}.External
                    AND Kind='payments' AND DirectKey IS NULL;
            """;
        m.Sql($"CREATE TRIGGER Directory_docs_i AFTER INSERT ON DirectoryXeroDocs BEGIN {Latest("NEW")} {Relink("NEW")} END;");
        m.Sql($"CREATE TRIGGER Directory_docs_d AFTER DELETE ON DirectoryXeroDocs BEGIN {Latest("OLD")} {Relink("OLD")} END;");
        m.Sql($"CREATE TRIGGER Directory_docs_u AFTER UPDATE OF ContactKey ON DirectoryXeroDocs WHEN OLD.ContactKey IS NOT NEW.ContactKey BEGIN {Latest("OLD")} {Latest("NEW")} END;");
        Triggers(m,"XeroMirrorRecords","OrganisationId,XeroConnectionId,ExternalId,DatasetKey,IsArchived,SourceJson",
            "DELETE FROM DirectoryXeroDocs WHERE Id=OLD.Id;",
            $"""
            INSERT INTO DirectoryXeroDocs SELECT s.Id,s.Org,s.Conn,s.Kind,s.External,s.DirectKey,s.ParentKey,
               coalesce(s.DirectKey,CASE WHEN s.Kind='payments' THEN {Resolve("s")} END),s.Used FROM DirectoryXeroSource s WHERE s.Id=NEW.Id;
            """,
            "WHEN OLD.OrganisationId IS NOT NEW.OrganisationId OR OLD.XeroConnectionId IS NOT NEW.XeroConnectionId OR OLD.ExternalId IS NOT NEW.ExternalId OR OLD.DatasetKey IS NOT NEW.DatasetKey OR OLD.IsArchived IS NOT NEW.IsArchived OR OLD.SourceJson IS NOT NEW.SourceJson");
    }

    protected override void Down(MigrationBuilder m)
    {
        foreach (var table in LocalSources.Select(x=>x.Table).Concat(["AuditEvents","XeroMigrationRecords","XeroMirrorRecords"]))
            foreach (var suffix in new[]{"i","u","d"})
                m.Sql($"DROP TRIGGER Directory_{table}_{suffix}");
        m.Sql("""
            DROP INDEX DirectoryXeroContactDates;
            DROP TABLE DirectoryXeroDocs;
            DROP TABLE DirectoryXeroLast;
            DROP VIEW DirectoryXeroSource;
            DROP TABLE DirectoryBeginnings;
            DROP TABLE DirectoryLocalLast;
            DROP VIEW DirectoryLocalTrades;
            """);
    }
}
