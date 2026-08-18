using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Migrations
{
    /// <inheritdoc />
    public partial class ProtectSalesHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoices_BusinessParties_CustomerId",
                table: "SalesInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoices_Organisations_OrganisationId",
                table: "SalesInvoices");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoices_BusinessParties_CustomerId",
                table: "SalesInvoices",
                column: "CustomerId",
                principalTable: "BusinessParties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoices_Organisations_OrganisationId",
                table: "SalesInvoices",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoices_BusinessParties_CustomerId",
                table: "SalesInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoices_Organisations_OrganisationId",
                table: "SalesInvoices");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoices_BusinessParties_CustomerId",
                table: "SalesInvoices",
                column: "CustomerId",
                principalTable: "BusinessParties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoices_Organisations_OrganisationId",
                table: "SalesInvoices",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
