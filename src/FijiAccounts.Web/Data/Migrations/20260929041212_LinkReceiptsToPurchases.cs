using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class LinkReceiptsToPurchases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LinkedSupplierBillDraftId",
                table: "EmployeeReceipts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LinkedSupplierBillId",
                table: "EmployeeReceipts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeReceipts_OrganisationId_LinkedSupplierBillDraftId",
                table: "EmployeeReceipts",
                columns: new[] { "OrganisationId", "LinkedSupplierBillDraftId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmployeeReceipts_OrganisationId_LinkedSupplierBillDraftId",
                table: "EmployeeReceipts");

            migrationBuilder.DropColumn(
                name: "LinkedSupplierBillDraftId",
                table: "EmployeeReceipts");

            migrationBuilder.DropColumn(
                name: "LinkedSupplierBillId",
                table: "EmployeeReceipts");
        }
    }
}
