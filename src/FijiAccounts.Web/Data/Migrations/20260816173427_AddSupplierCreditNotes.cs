using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierCreditNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AmountCredited",
                table: "SupplierBills",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "SupplierCreditNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SupplierBillId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SequenceNumber = table.Column<long>(type: "INTEGER", nullable: false),
                    CreditNoteNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CreditDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Subtotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    VatTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ReturnedTrackedItems = table.Column<bool>(type: "INTEGER", nullable: false),
                    PostedJournalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierCreditNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierCreditNotes_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierCreditNotes_PostedJournals_PostedJournalId",
                        column: x => x.PostedJournalId,
                        principalTable: "PostedJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierCreditNotes_SupplierBills_SupplierBillId",
                        column: x => x.SupplierBillId,
                        principalTable: "SupplierBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditNotes_OrganisationId_CreditNoteNumber",
                table: "SupplierCreditNotes",
                columns: new[] { "OrganisationId", "CreditNoteNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditNotes_OrganisationId_SequenceNumber",
                table: "SupplierCreditNotes",
                columns: new[] { "OrganisationId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditNotes_PostedJournalId",
                table: "SupplierCreditNotes",
                column: "PostedJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditNotes_SupplierBillId",
                table: "SupplierCreditNotes",
                column: "SupplierBillId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupplierCreditNotes");

            migrationBuilder.DropColumn(
                name: "AmountCredited",
                table: "SupplierBills");
        }
    }
}
