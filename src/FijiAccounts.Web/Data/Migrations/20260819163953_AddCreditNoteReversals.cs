using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditNoteReversals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SalesCreditNoteReversals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SalesCreditNoteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReversalDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    PostedJournalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesCreditNoteReversals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesCreditNoteReversals_PostedJournals_PostedJournalId",
                        column: x => x.PostedJournalId,
                        principalTable: "PostedJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesCreditNoteReversals_SalesCreditNotes_SalesCreditNoteId",
                        column: x => x.SalesCreditNoteId,
                        principalTable: "SalesCreditNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierCreditNoteReversals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SupplierCreditNoteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReversalDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    PostedJournalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierCreditNoteReversals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierCreditNoteReversals_PostedJournals_PostedJournalId",
                        column: x => x.PostedJournalId,
                        principalTable: "PostedJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierCreditNoteReversals_SupplierCreditNotes_SupplierCreditNoteId",
                        column: x => x.SupplierCreditNoteId,
                        principalTable: "SupplierCreditNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesCreditNoteReversals_PostedJournalId",
                table: "SalesCreditNoteReversals",
                column: "PostedJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesCreditNoteReversals_SalesCreditNoteId",
                table: "SalesCreditNoteReversals",
                column: "SalesCreditNoteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditNoteReversals_PostedJournalId",
                table: "SupplierCreditNoteReversals",
                column: "PostedJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditNoteReversals_SupplierCreditNoteId",
                table: "SupplierCreditNoteReversals",
                column: "SupplierCreditNoteId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesCreditNoteReversals");

            migrationBuilder.DropTable(
                name: "SupplierCreditNoteReversals");
        }
    }
}
