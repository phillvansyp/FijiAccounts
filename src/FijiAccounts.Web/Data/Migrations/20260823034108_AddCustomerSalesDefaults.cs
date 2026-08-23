using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerSalesDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DefaultSalesAccountId",
                table: "BusinessParties",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultSalesVatTreatment",
                table: "BusinessParties",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE BusinessParties
                SET DefaultSalesVatTreatment = DefaultPurchaseVatTreatment
                WHERE (Type & 1) <> 0;
                """);

            migrationBuilder.Sql(
                """
                UPDATE BusinessParties
                SET DefaultSalesAccountId = DefaultPurchaseAccountId
                WHERE (Type & 1) <> 0
                  AND EXISTS (
                      SELECT 1
                      FROM LedgerAccounts
                      WHERE LedgerAccounts.Id = BusinessParties.DefaultPurchaseAccountId
                        AND LedgerAccounts.Type = 3
                  );
                """);

            migrationBuilder.Sql(
                """
                UPDATE BusinessParties
                SET DefaultPurchaseAccountId = NULL,
                    DefaultPurchaseVatTreatment = NULL
                WHERE Type = 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessParties_DefaultSalesAccountId",
                table: "BusinessParties",
                column: "DefaultSalesAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessParties_LedgerAccounts_DefaultSalesAccountId",
                table: "BusinessParties",
                column: "DefaultSalesAccountId",
                principalTable: "LedgerAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BusinessParties_LedgerAccounts_DefaultSalesAccountId",
                table: "BusinessParties");

            migrationBuilder.DropIndex(
                name: "IX_BusinessParties_DefaultSalesAccountId",
                table: "BusinessParties");

            migrationBuilder.DropColumn(
                name: "DefaultSalesAccountId",
                table: "BusinessParties");

            migrationBuilder.DropColumn(
                name: "DefaultSalesVatTreatment",
                table: "BusinessParties");
        }
    }
}
