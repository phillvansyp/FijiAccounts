using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Migrations
{
    /// <inheritdoc />
    public partial class LinkInventoryToTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProductItemId",
                table: "SupplierBillLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProductItemId",
                table: "SalesInvoiceLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBillLines_ProductItemId",
                table: "SupplierBillLines",
                column: "ProductItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_ProductItemId",
                table: "SalesInvoiceLines",
                column: "ProductItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceLines_ProductItems_ProductItemId",
                table: "SalesInvoiceLines",
                column: "ProductItemId",
                principalTable: "ProductItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SupplierBillLines_ProductItems_ProductItemId",
                table: "SupplierBillLines",
                column: "ProductItemId",
                principalTable: "ProductItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceLines_ProductItems_ProductItemId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SupplierBillLines_ProductItems_ProductItemId",
                table: "SupplierBillLines");

            migrationBuilder.DropIndex(
                name: "IX_SupplierBillLines_ProductItemId",
                table: "SupplierBillLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceLines_ProductItemId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "ProductItemId",
                table: "SupplierBillLines");

            migrationBuilder.DropColumn(
                name: "ProductItemId",
                table: "SalesInvoiceLines");
        }
    }
}
