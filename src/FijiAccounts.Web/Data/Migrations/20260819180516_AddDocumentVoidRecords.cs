using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentVoidRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SalesInvoiceVoids",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SalesInvoiceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VoidDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PostedJournalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoiceVoids", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceVoids_PostedJournals_PostedJournalId",
                        column: x => x.PostedJournalId,
                        principalTable: "PostedJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceVoids_SalesInvoices_SalesInvoiceId",
                        column: x => x.SalesInvoiceId,
                        principalTable: "SalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierBillVoids",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SupplierBillId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VoidDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    PostedJournalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierBillVoids", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierBillVoids_PostedJournals_PostedJournalId",
                        column: x => x.PostedJournalId,
                        principalTable: "PostedJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierBillVoids_SupplierBills_SupplierBillId",
                        column: x => x.SupplierBillId,
                        principalTable: "SupplierBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceVoids_PostedJournalId",
                table: "SalesInvoiceVoids",
                column: "PostedJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceVoids_SalesInvoiceId",
                table: "SalesInvoiceVoids",
                column: "SalesInvoiceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBillVoids_PostedJournalId",
                table: "SupplierBillVoids",
                column: "PostedJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBillVoids_SupplierBillId",
                table: "SupplierBillVoids",
                column: "SupplierBillId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesInvoiceVoids");

            migrationBuilder.DropTable(
                name: "SupplierBillVoids");
        }
    }
}
