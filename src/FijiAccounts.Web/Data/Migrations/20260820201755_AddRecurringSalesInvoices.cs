using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringSalesInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecurringSalesInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Frequency = table.Column<int>(type: "INTEGER", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    NextInvoiceDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DueDays = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringSalesInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringSalesInvoices_BusinessParties_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "BusinessParties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecurringSalesInvoiceGenerations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecurringSalesInvoiceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScheduledDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    SalesInvoiceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    GeneratedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringSalesInvoiceGenerations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringSalesInvoiceGenerations_RecurringSalesInvoices_RecurringSalesInvoiceId",
                        column: x => x.RecurringSalesInvoiceId,
                        principalTable: "RecurringSalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringSalesInvoiceGenerations_SalesInvoices_SalesInvoiceId",
                        column: x => x.SalesInvoiceId,
                        principalTable: "SalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecurringSalesInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecurringSalesInvoiceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    VatTreatment = table.Column<int>(type: "INTEGER", nullable: false),
                    RevenueAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductItemId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringSalesInvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringSalesInvoiceLines_LedgerAccounts_RevenueAccountId",
                        column: x => x.RevenueAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringSalesInvoiceLines_ProductItems_ProductItemId",
                        column: x => x.ProductItemId,
                        principalTable: "ProductItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringSalesInvoiceLines_RecurringSalesInvoices_RecurringSalesInvoiceId",
                        column: x => x.RecurringSalesInvoiceId,
                        principalTable: "RecurringSalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSalesInvoiceGenerations_RecurringSalesInvoiceId_ScheduledDate",
                table: "RecurringSalesInvoiceGenerations",
                columns: new[] { "RecurringSalesInvoiceId", "ScheduledDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSalesInvoiceGenerations_SalesInvoiceId",
                table: "RecurringSalesInvoiceGenerations",
                column: "SalesInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSalesInvoiceLines_ProductItemId",
                table: "RecurringSalesInvoiceLines",
                column: "ProductItemId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSalesInvoiceLines_RecurringSalesInvoiceId",
                table: "RecurringSalesInvoiceLines",
                column: "RecurringSalesInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSalesInvoiceLines_RevenueAccountId",
                table: "RecurringSalesInvoiceLines",
                column: "RevenueAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSalesInvoices_CustomerId",
                table: "RecurringSalesInvoices",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSalesInvoices_OrganisationId_NextInvoiceDate",
                table: "RecurringSalesInvoices",
                columns: new[] { "OrganisationId", "NextInvoiceDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecurringSalesInvoiceGenerations");

            migrationBuilder.DropTable(
                name: "RecurringSalesInvoiceLines");

            migrationBuilder.DropTable(
                name: "RecurringSalesInvoices");
        }
    }
}
