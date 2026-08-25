using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class CompleteEnterpriseDimensionHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganisationUnits");

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "InventoryMovements",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "DivisionId",
                table: "InventoryMovements",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "BankTransfers",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "DivisionId",
                table: "BankTransfers",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql(
                """
                UPDATE InventoryMovements
                SET BranchId = (
                        SELECT BranchId FROM PostedJournalLines
                        WHERE PostedJournalId = InventoryMovements.PostedJournalId
                          AND BranchId IS NOT NULL
                        ORDER BY Id LIMIT 1),
                    DivisionId = (
                        SELECT DivisionId FROM PostedJournalLines
                        WHERE PostedJournalId = InventoryMovements.PostedJournalId
                          AND DivisionId IS NOT NULL
                        ORDER BY Id LIMIT 1);

                UPDATE BankTransfers
                SET BranchId = (
                        SELECT BranchId FROM PostedJournalLines
                        WHERE PostedJournalId = BankTransfers.PostedJournalId
                          AND BranchId IS NOT NULL
                        ORDER BY Id LIMIT 1),
                    DivisionId = (
                        SELECT DivisionId FROM PostedJournalLines
                        WHERE PostedJournalId = BankTransfers.PostedJournalId
                          AND DivisionId IS NOT NULL
                        ORDER BY Id LIMIT 1);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_BranchId_DivisionId",
                table: "InventoryMovements",
                columns: new[] { "BranchId", "DivisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_DivisionId",
                table: "InventoryMovements",
                column: "DivisionId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransfers_BranchId_DivisionId",
                table: "BankTransfers",
                columns: new[] { "BranchId", "DivisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_BankTransfers_DivisionId",
                table: "BankTransfers",
                column: "DivisionId");

            migrationBuilder.AddForeignKey(
                name: "FK_BankTransfers_Branches_BranchId",
                table: "BankTransfers",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BankTransfers_Divisions_DivisionId",
                table: "BankTransfers",
                column: "DivisionId",
                principalTable: "Divisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_Branches_BranchId",
                table: "InventoryMovements",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_Divisions_DivisionId",
                table: "InventoryMovements",
                column: "DivisionId",
                principalTable: "Divisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BankTransfers_Branches_BranchId",
                table: "BankTransfers");

            migrationBuilder.DropForeignKey(
                name: "FK_BankTransfers_Divisions_DivisionId",
                table: "BankTransfers");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryMovements_Branches_BranchId",
                table: "InventoryMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryMovements_Divisions_DivisionId",
                table: "InventoryMovements");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_BranchId_DivisionId",
                table: "InventoryMovements");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_DivisionId",
                table: "InventoryMovements");

            migrationBuilder.DropIndex(
                name: "IX_BankTransfers_BranchId_DivisionId",
                table: "BankTransfers");

            migrationBuilder.DropIndex(
                name: "IX_BankTransfers_DivisionId",
                table: "BankTransfers");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "DivisionId",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "BankTransfers");

            migrationBuilder.DropColumn(
                name: "DivisionId",
                table: "BankTransfers");

            migrationBuilder.CreateTable(
                name: "OrganisationUnits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganisationUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganisationUnits_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganisationUnits_OrganisationId_Type_Code",
                table: "OrganisationUnits",
                columns: new[] { "OrganisationId", "Type", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganisationUnits_OrganisationId_Type_Name",
                table: "OrganisationUnits",
                columns: new[] { "OrganisationId", "Type", "Name" },
                unique: true);
        }
    }
}
