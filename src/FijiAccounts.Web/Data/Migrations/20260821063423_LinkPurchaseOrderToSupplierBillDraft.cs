using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class LinkPurchaseOrderToSupplierBillDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SupplierBillDraftId",
                table: "PurchaseOrders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_SupplierBillDraftId",
                table: "PurchaseOrders",
                column: "SupplierBillDraftId");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_SupplierBillDrafts_SupplierBillDraftId",
                table: "PurchaseOrders",
                column: "SupplierBillDraftId",
                principalTable: "SupplierBillDrafts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_SupplierBillDrafts_SupplierBillDraftId",
                table: "PurchaseOrders");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_SupplierBillDraftId",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "SupplierBillDraftId",
                table: "PurchaseOrders");
        }
    }
}
