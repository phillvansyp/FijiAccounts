using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierTaxAndAccountNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SupplierAccountNumber",
                table: "BusinessParties",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatRegistrationNumber",
                table: "BusinessParties",
                type: "TEXT",
                maxLength: 80,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SupplierAccountNumber",
                table: "BusinessParties");

            migrationBuilder.DropColumn(
                name: "VatRegistrationNumber",
                table: "BusinessParties");
        }
    }
}
