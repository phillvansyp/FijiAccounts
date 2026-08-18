using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierBillDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupplierBillDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SupplierId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SupplierReference = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    BillDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    VatTreatment = table.Column<int>(type: "INTEGER", nullable: false),
                    ExpenseAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProductItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AdditionalLinesJson = table.Column<string>(type: "TEXT", nullable: false),
                    AttachmentFileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    AttachmentContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    AttachmentOriginalSize = table.Column<long>(type: "INTEGER", nullable: true),
                    AttachmentIsCompressed = table.Column<bool>(type: "INTEGER", nullable: false),
                    AttachmentContent = table.Column<byte[]>(type: "BLOB", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierBillDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierBillDrafts_BusinessParties_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "BusinessParties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBillDrafts_OrganisationId_UpdatedAt",
                table: "SupplierBillDrafts",
                columns: new[] { "OrganisationId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBillDrafts_SupplierId",
                table: "SupplierBillDrafts",
                column: "SupplierId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupplierBillDrafts");
        }
    }
}
