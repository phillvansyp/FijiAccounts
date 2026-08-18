using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierPurchaseDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DefaultPurchaseAccountId",
                table: "BusinessParties",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultPurchaseVatTreatment",
                table: "BusinessParties",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessParties_DefaultPurchaseAccountId",
                table: "BusinessParties",
                column: "DefaultPurchaseAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessParties_LedgerAccounts_DefaultPurchaseAccountId",
                table: "BusinessParties",
                column: "DefaultPurchaseAccountId",
                principalTable: "LedgerAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BusinessParties_LedgerAccounts_DefaultPurchaseAccountId",
                table: "BusinessParties");

            migrationBuilder.DropIndex(
                name: "IX_BusinessParties_DefaultPurchaseAccountId",
                table: "BusinessParties");

            migrationBuilder.DropColumn(
                name: "DefaultPurchaseAccountId",
                table: "BusinessParties");

            migrationBuilder.DropColumn(
                name: "DefaultPurchaseVatTreatment",
                table: "BusinessParties");
        }
    }
}
