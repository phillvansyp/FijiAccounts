using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AverageCost",
                table: "ProductItems",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "CostAdjustmentAccountId",
                table: "ProductItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InventoryAccountId",
                table: "ProductItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "QuantityOnHand",
                table: "ProductItems",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReorderLevel",
                table: "ProductItems",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "InventoryMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MovementDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    QuantityChange = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ValueChange = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    PostedJournalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PostedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_PostedJournals_PostedJournalId",
                        column: x => x.PostedJournalId,
                        principalTable: "PostedJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_ProductItems_ProductItemId",
                        column: x => x.ProductItemId,
                        principalTable: "ProductItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductItems_CostAdjustmentAccountId",
                table: "ProductItems",
                column: "CostAdjustmentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductItems_InventoryAccountId",
                table: "ProductItems",
                column: "InventoryAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_OrganisationId_ProductItemId_MovementDate",
                table: "InventoryMovements",
                columns: new[] { "OrganisationId", "ProductItemId", "MovementDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_PostedJournalId",
                table: "InventoryMovements",
                column: "PostedJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_ProductItemId",
                table: "InventoryMovements",
                column: "ProductItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductItems_LedgerAccounts_CostAdjustmentAccountId",
                table: "ProductItems",
                column: "CostAdjustmentAccountId",
                principalTable: "LedgerAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductItems_LedgerAccounts_InventoryAccountId",
                table: "ProductItems",
                column: "InventoryAccountId",
                principalTable: "LedgerAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductItems_LedgerAccounts_CostAdjustmentAccountId",
                table: "ProductItems");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductItems_LedgerAccounts_InventoryAccountId",
                table: "ProductItems");

            migrationBuilder.DropTable(
                name: "InventoryMovements");

            migrationBuilder.DropIndex(
                name: "IX_ProductItems_CostAdjustmentAccountId",
                table: "ProductItems");

            migrationBuilder.DropIndex(
                name: "IX_ProductItems_InventoryAccountId",
                table: "ProductItems");

            migrationBuilder.DropColumn(
                name: "AverageCost",
                table: "ProductItems");

            migrationBuilder.DropColumn(
                name: "CostAdjustmentAccountId",
                table: "ProductItems");

            migrationBuilder.DropColumn(
                name: "InventoryAccountId",
                table: "ProductItems");

            migrationBuilder.DropColumn(
                name: "QuantityOnHand",
                table: "ProductItems");

            migrationBuilder.DropColumn(
                name: "ReorderLevel",
                table: "ProductItems");
        }
    }
}
