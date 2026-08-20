using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringSupplierBills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecurringSupplierBills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SupplierId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SupplierReference = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Frequency = table.Column<int>(type: "INTEGER", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    NextBillDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DueDays = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringSupplierBills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringSupplierBills_BusinessParties_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "BusinessParties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecurringSupplierBillGenerations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecurringSupplierBillId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScheduledDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    SupplierBillId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    GeneratedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringSupplierBillGenerations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringSupplierBillGenerations_RecurringSupplierBills_RecurringSupplierBillId",
                        column: x => x.RecurringSupplierBillId,
                        principalTable: "RecurringSupplierBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringSupplierBillGenerations_SupplierBills_SupplierBillId",
                        column: x => x.SupplierBillId,
                        principalTable: "SupplierBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecurringSupplierBillLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecurringSupplierBillId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    VatTreatment = table.Column<int>(type: "INTEGER", nullable: false),
                    ExpenseAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductItemId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringSupplierBillLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringSupplierBillLines_LedgerAccounts_ExpenseAccountId",
                        column: x => x.ExpenseAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringSupplierBillLines_ProductItems_ProductItemId",
                        column: x => x.ProductItemId,
                        principalTable: "ProductItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringSupplierBillLines_RecurringSupplierBills_RecurringSupplierBillId",
                        column: x => x.RecurringSupplierBillId,
                        principalTable: "RecurringSupplierBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSupplierBillGenerations_RecurringSupplierBillId_ScheduledDate",
                table: "RecurringSupplierBillGenerations",
                columns: new[] { "RecurringSupplierBillId", "ScheduledDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSupplierBillGenerations_SupplierBillId",
                table: "RecurringSupplierBillGenerations",
                column: "SupplierBillId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSupplierBillLines_ExpenseAccountId",
                table: "RecurringSupplierBillLines",
                column: "ExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSupplierBillLines_ProductItemId",
                table: "RecurringSupplierBillLines",
                column: "ProductItemId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSupplierBillLines_RecurringSupplierBillId",
                table: "RecurringSupplierBillLines",
                column: "RecurringSupplierBillId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSupplierBills_OrganisationId_NextBillDate",
                table: "RecurringSupplierBills",
                columns: new[] { "OrganisationId", "NextBillDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSupplierBills_SupplierId",
                table: "RecurringSupplierBills",
                column: "SupplierId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecurringSupplierBillGenerations");

            migrationBuilder.DropTable(
                name: "RecurringSupplierBillLines");

            migrationBuilder.DropTable(
                name: "RecurringSupplierBills");
        }
    }
}
