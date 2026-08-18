using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddProductCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    SalePrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    PurchasePrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    SaleTaxTreatment = table.Column<int>(type: "INTEGER", nullable: false),
                    PurchaseTaxTreatment = table.Column<int>(type: "INTEGER", nullable: false),
                    RevenueAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ExpenseAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductItems_LedgerAccounts_ExpenseAccountId",
                        column: x => x.ExpenseAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductItems_LedgerAccounts_RevenueAccountId",
                        column: x => x.RevenueAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductItems_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductItems_ExpenseAccountId",
                table: "ProductItems",
                column: "ExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductItems_OrganisationId_Code",
                table: "ProductItems",
                columns: new[] { "OrganisationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductItems_RevenueAccountId",
                table: "ProductItems",
                column: "RevenueAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductItems");
        }
    }
}
