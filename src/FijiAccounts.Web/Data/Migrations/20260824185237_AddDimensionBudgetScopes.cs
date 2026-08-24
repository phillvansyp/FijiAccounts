using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDimensionBudgetScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccountBudgets_OrganisationId_LedgerAccountId_Month",
                table: "AccountBudgets");

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "AccountBudgets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DivisionId",
                table: "AccountBudgets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScopeKey",
                table: "AccountBudgets",
                type: "TEXT",
                maxLength: 80,
                nullable: false,
                defaultValue: "organisation");

            migrationBuilder.CreateIndex(
                name: "IX_AccountBudgets_BranchId",
                table: "AccountBudgets",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountBudgets_DivisionId",
                table: "AccountBudgets",
                column: "DivisionId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountBudgets_OrganisationId_LedgerAccountId_Month_ScopeKey",
                table: "AccountBudgets",
                columns: new[] { "OrganisationId", "LedgerAccountId", "Month", "ScopeKey" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AccountBudgets_Branches_BranchId",
                table: "AccountBudgets",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AccountBudgets_Divisions_DivisionId",
                table: "AccountBudgets",
                column: "DivisionId",
                principalTable: "Divisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DELETE FROM AccountBudgets WHERE ScopeKey <> 'organisation'");

            migrationBuilder.DropForeignKey(
                name: "FK_AccountBudgets_Branches_BranchId",
                table: "AccountBudgets");

            migrationBuilder.DropForeignKey(
                name: "FK_AccountBudgets_Divisions_DivisionId",
                table: "AccountBudgets");

            migrationBuilder.DropIndex(
                name: "IX_AccountBudgets_BranchId",
                table: "AccountBudgets");

            migrationBuilder.DropIndex(
                name: "IX_AccountBudgets_DivisionId",
                table: "AccountBudgets");

            migrationBuilder.DropIndex(
                name: "IX_AccountBudgets_OrganisationId_LedgerAccountId_Month_ScopeKey",
                table: "AccountBudgets");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "AccountBudgets");

            migrationBuilder.DropColumn(
                name: "DivisionId",
                table: "AccountBudgets");

            migrationBuilder.DropColumn(
                name: "ScopeKey",
                table: "AccountBudgets");

            migrationBuilder.CreateIndex(
                name: "IX_AccountBudgets_OrganisationId_LedgerAccountId_Month",
                table: "AccountBudgets",
                columns: new[] { "OrganisationId", "LedgerAccountId", "Month" },
                unique: true);
        }
    }
}
