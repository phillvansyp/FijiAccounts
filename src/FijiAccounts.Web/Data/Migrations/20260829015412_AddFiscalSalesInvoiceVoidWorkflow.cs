using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFiscalSalesInvoiceVoidWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "PostedJournalId",
                table: "SalesInvoiceVoids",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "SalesInvoiceVoids",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesInvoiceVoidId",
                table: "FiscalisationRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FiscalisationRecords_SalesInvoiceVoidId",
                table: "FiscalisationRecords",
                column: "SalesInvoiceVoidId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_FiscalisationRecords_SalesInvoiceVoids_SalesInvoiceVoidId",
                table: "FiscalisationRecords",
                column: "SalesInvoiceVoidId",
                principalTable: "SalesInvoiceVoids",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FiscalisationRecords_SalesInvoiceVoids_SalesInvoiceVoidId",
                table: "FiscalisationRecords");

            migrationBuilder.DropIndex(
                name: "IX_FiscalisationRecords_SalesInvoiceVoidId",
                table: "FiscalisationRecords");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "SalesInvoiceVoids");

            migrationBuilder.DropColumn(
                name: "SalesInvoiceVoidId",
                table: "FiscalisationRecords");

            migrationBuilder.AlterColumn<Guid>(
                name: "PostedJournalId",
                table: "SalesInvoiceVoids",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
