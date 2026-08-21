using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFlexiblePaymentTermTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultSalesInvoicePaymentTermType",
                table: "Organisations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DefaultSupplierBillPaymentTermType",
                table: "Organisations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DefaultSalesInvoicePaymentTermType",
                table: "BusinessParties",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DefaultSupplierBillPaymentTermType",
                table: "BusinessParties",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultSalesInvoicePaymentTermType",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "DefaultSupplierBillPaymentTermType",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "DefaultSalesInvoicePaymentTermType",
                table: "BusinessParties");

            migrationBuilder.DropColumn(
                name: "DefaultSupplierBillPaymentTermType",
                table: "BusinessParties");
        }
    }
}
