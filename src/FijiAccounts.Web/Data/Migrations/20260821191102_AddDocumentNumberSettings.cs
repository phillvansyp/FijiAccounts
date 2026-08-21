using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentNumberSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "NextPurchaseOrderNumber",
                table: "Organisations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "NextSalesCreditNoteNumber",
                table: "Organisations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "NextSalesInvoiceNumber",
                table: "Organisations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "NextSalesQuoteNumber",
                table: "Organisations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "NextSupplierBillNumber",
                table: "Organisations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "NextSupplierCreditNoteNumber",
                table: "Organisations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<string>(
                name: "PurchaseOrderPrefix",
                table: "Organisations",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "PO-");

            migrationBuilder.AddColumn<string>(
                name: "SalesCreditNotePrefix",
                table: "Organisations",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "CN-");

            migrationBuilder.AddColumn<string>(
                name: "SalesInvoicePrefix",
                table: "Organisations",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "INV-");

            migrationBuilder.AddColumn<string>(
                name: "SalesQuotePrefix",
                table: "Organisations",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "QU-");

            migrationBuilder.AddColumn<string>(
                name: "SupplierBillPrefix",
                table: "Organisations",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "BILL-");

            migrationBuilder.AddColumn<string>(
                name: "SupplierCreditNotePrefix",
                table: "Organisations",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "SCN-");

            migrationBuilder.Sql(
                """
                UPDATE Organisations
                SET NextSalesInvoiceNumber =
                    COALESCE((
                        SELECT MAX(SequenceNumber) + 1
                        FROM SalesInvoices
                        WHERE SalesInvoices.OrganisationId = Organisations.Id
                    ), 1),
                    NextSalesQuoteNumber =
                    COALESCE((
                        SELECT MAX(SequenceNumber) + 1
                        FROM SalesQuotes
                        WHERE SalesQuotes.OrganisationId = Organisations.Id
                    ), 1),
                    NextSalesCreditNoteNumber =
                    COALESCE((
                        SELECT MAX(SequenceNumber) + 1
                        FROM SalesCreditNotes
                        WHERE SalesCreditNotes.OrganisationId = Organisations.Id
                    ), 1),
                    NextPurchaseOrderNumber =
                    COALESCE((
                        SELECT MAX(SequenceNumber) + 1
                        FROM PurchaseOrders
                        WHERE PurchaseOrders.OrganisationId = Organisations.Id
                    ), 1),
                    NextSupplierBillNumber =
                    COALESCE((
                        SELECT MAX(SequenceNumber) + 1
                        FROM SupplierBills
                        WHERE SupplierBills.OrganisationId = Organisations.Id
                    ), 1),
                    NextSupplierCreditNoteNumber =
                    COALESCE((
                        SELECT MAX(SequenceNumber) + 1
                        FROM SupplierCreditNotes
                        WHERE SupplierCreditNotes.OrganisationId = Organisations.Id
                    ), 1);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NextPurchaseOrderNumber",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "NextSalesCreditNoteNumber",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "NextSalesInvoiceNumber",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "NextSalesQuoteNumber",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "NextSupplierBillNumber",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "NextSupplierCreditNoteNumber",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "PurchaseOrderPrefix",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "SalesCreditNotePrefix",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "SalesInvoicePrefix",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "SalesQuotePrefix",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "SupplierBillPrefix",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "SupplierCreditNotePrefix",
                table: "Organisations");
        }
    }
}
