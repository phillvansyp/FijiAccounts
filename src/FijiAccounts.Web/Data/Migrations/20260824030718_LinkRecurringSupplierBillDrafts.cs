using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class LinkRecurringSupplierBillDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "SupplierBillId",
                table: "RecurringSupplierBillGenerations",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<Guid>(
                name: "SupplierBillDraftId",
                table: "RecurringSupplierBillGenerations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSupplierBillGenerations_SupplierBillDraftId",
                table: "RecurringSupplierBillGenerations",
                column: "SupplierBillDraftId");

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringSupplierBillGenerations_SupplierBillDrafts_SupplierBillDraftId",
                table: "RecurringSupplierBillGenerations",
                column: "SupplierBillDraftId",
                principalTable: "SupplierBillDrafts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RecurringSupplierBillGenerations_SupplierBillDrafts_SupplierBillDraftId",
                table: "RecurringSupplierBillGenerations");

            migrationBuilder.DropIndex(
                name: "IX_RecurringSupplierBillGenerations_SupplierBillDraftId",
                table: "RecurringSupplierBillGenerations");

            migrationBuilder.DropColumn(
                name: "SupplierBillDraftId",
                table: "RecurringSupplierBillGenerations");

            migrationBuilder.AlterColumn<Guid>(
                name: "SupplierBillId",
                table: "RecurringSupplierBillGenerations",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
