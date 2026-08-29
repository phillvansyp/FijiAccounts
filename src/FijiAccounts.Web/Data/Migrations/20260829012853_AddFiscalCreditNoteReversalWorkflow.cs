using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFiscalCreditNoteReversalWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "PostedJournalId",
                table: "SalesCreditNoteReversals",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "SalesCreditNoteReversals",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesCreditNoteReversalId",
                table: "FiscalisationRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FiscalisationRecords_SalesCreditNoteReversalId",
                table: "FiscalisationRecords",
                column: "SalesCreditNoteReversalId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_FiscalisationRecords_SalesCreditNoteReversals_SalesCreditNoteReversalId",
                table: "FiscalisationRecords",
                column: "SalesCreditNoteReversalId",
                principalTable: "SalesCreditNoteReversals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FiscalisationRecords_SalesCreditNoteReversals_SalesCreditNoteReversalId",
                table: "FiscalisationRecords");

            migrationBuilder.DropIndex(
                name: "IX_FiscalisationRecords_SalesCreditNoteReversalId",
                table: "FiscalisationRecords");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "SalesCreditNoteReversals");

            migrationBuilder.DropColumn(
                name: "SalesCreditNoteReversalId",
                table: "FiscalisationRecords");

            migrationBuilder.AlterColumn<Guid>(
                name: "PostedJournalId",
                table: "SalesCreditNoteReversals",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
