using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class SupportStandaloneSupplierCredits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "SupplierBillId",
                table: "SupplierCreditNotes",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<Guid>(
                name: "SupplierId",
                table: "SupplierCreditNotes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditNotes_SupplierId",
                table: "SupplierCreditNotes",
                column: "SupplierId");

            migrationBuilder.AddForeignKey(
                name: "FK_SupplierCreditNotes_BusinessParties_SupplierId",
                table: "SupplierCreditNotes",
                column: "SupplierId",
                principalTable: "BusinessParties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SupplierCreditNotes_BusinessParties_SupplierId",
                table: "SupplierCreditNotes");

            migrationBuilder.DropIndex(
                name: "IX_SupplierCreditNotes_SupplierId",
                table: "SupplierCreditNotes");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "SupplierCreditNotes");

            migrationBuilder.AlterColumn<Guid>(
                name: "SupplierBillId",
                table: "SupplierCreditNotes",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
