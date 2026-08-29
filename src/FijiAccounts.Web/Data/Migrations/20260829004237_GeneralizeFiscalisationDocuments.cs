using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class GeneralizeFiscalisationDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "SalesInvoiceId",
                table: "FiscalisationRecords",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<Guid>(
                name: "SalesCreditNoteId",
                table: "FiscalisationRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceDocumentKind",
                table: "FiscalisationRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_FiscalisationRecords_SalesCreditNoteId",
                table: "FiscalisationRecords",
                column: "SalesCreditNoteId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_FiscalisationRecords_SalesCreditNotes_SalesCreditNoteId",
                table: "FiscalisationRecords",
                column: "SalesCreditNoteId",
                principalTable: "SalesCreditNotes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FiscalisationRecords_SalesCreditNotes_SalesCreditNoteId",
                table: "FiscalisationRecords");

            migrationBuilder.DropIndex(
                name: "IX_FiscalisationRecords_SalesCreditNoteId",
                table: "FiscalisationRecords");

            migrationBuilder.DropColumn(
                name: "SalesCreditNoteId",
                table: "FiscalisationRecords");

            migrationBuilder.DropColumn(
                name: "SourceDocumentKind",
                table: "FiscalisationRecords");

            migrationBuilder.AlterColumn<Guid>(
                name: "SalesInvoiceId",
                table: "FiscalisationRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
