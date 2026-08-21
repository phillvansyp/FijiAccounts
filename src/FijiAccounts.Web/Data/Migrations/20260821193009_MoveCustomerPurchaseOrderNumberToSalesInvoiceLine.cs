using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class MoveCustomerPurchaseOrderNumberToSalesInvoiceLine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomerPurchaseOrderNumber",
                table: "SalesInvoices");

            migrationBuilder.AddColumn<string>(
                name: "CustomerPurchaseOrderNumber",
                table: "SalesInvoiceLines",
                type: "TEXT",
                maxLength: 80,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomerPurchaseOrderNumber",
                table: "SalesInvoiceLines");

            migrationBuilder.AddColumn<string>(
                name: "CustomerPurchaseOrderNumber",
                table: "SalesInvoices",
                type: "TEXT",
                maxLength: 80,
                nullable: true);
        }
    }
}
