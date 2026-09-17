using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierBillReinstatement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupplierBillReinstatements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SupplierBillId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SupplierBillVoidId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReinstatementDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    PostedJournalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierBillReinstatements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierBillReinstatements_PostedJournals_PostedJournalId",
                        column: x => x.PostedJournalId,
                        principalTable: "PostedJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierBillReinstatements_SupplierBillVoids_SupplierBillVoidId",
                        column: x => x.SupplierBillVoidId,
                        principalTable: "SupplierBillVoids",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierBillReinstatements_SupplierBills_SupplierBillId",
                        column: x => x.SupplierBillId,
                        principalTable: "SupplierBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBillReinstatements_PostedJournalId",
                table: "SupplierBillReinstatements",
                column: "PostedJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBillReinstatements_SupplierBillId",
                table: "SupplierBillReinstatements",
                column: "SupplierBillId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBillReinstatements_SupplierBillVoidId",
                table: "SupplierBillReinstatements",
                column: "SupplierBillVoidId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupplierBillReinstatements");
        }
    }
}
