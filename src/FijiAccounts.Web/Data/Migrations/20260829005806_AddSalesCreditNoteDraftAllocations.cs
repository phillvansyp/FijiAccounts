using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesCreditNoteDraftAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "PostedJournalId",
                table: "SalesCreditNotes",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<bool>(
                name: "RestockTrackedItems",
                table: "SalesCreditNotes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "SalesCreditNotes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "SalesCreditNoteLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SalesCreditNoteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SalesInvoiceLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    VatTreatment = table.Column<int>(type: "INTEGER", nullable: false),
                    VatRate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    NetAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    VatAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    GrossAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    RevenueAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProjectCostCodeId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesCreditNoteLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesCreditNoteLines_SalesCreditNotes_SalesCreditNoteId",
                        column: x => x.SalesCreditNoteId,
                        principalTable: "SalesCreditNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SalesCreditNoteLines_SalesInvoiceLines_SalesInvoiceLineId",
                        column: x => x.SalesInvoiceLineId,
                        principalTable: "SalesInvoiceLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesCreditNoteLines_SalesCreditNoteId_SalesInvoiceLineId",
                table: "SalesCreditNoteLines",
                columns: new[] { "SalesCreditNoteId", "SalesInvoiceLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesCreditNoteLines_SalesInvoiceLineId",
                table: "SalesCreditNoteLines",
                column: "SalesInvoiceLineId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesCreditNoteLines");

            migrationBuilder.DropColumn(
                name: "RestockTrackedItems",
                table: "SalesCreditNotes");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "SalesCreditNotes");

            migrationBuilder.AlterColumn<Guid>(
                name: "PostedJournalId",
                table: "SalesCreditNotes",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
