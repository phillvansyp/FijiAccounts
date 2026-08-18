using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddIslandJurisdictions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                table: "Organisations",
                type: "TEXT",
                maxLength: 2,
                nullable: false,
                defaultValue: "FJ");

            migrationBuilder.AddColumn<int>(
                name: "FinancialYearEndDay",
                table: "Organisations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 31);

            migrationBuilder.AddColumn<int>(
                name: "FinancialYearEndMonth",
                table: "Organisations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 12);

            migrationBuilder.AddColumn<string>(
                name: "TaxLabel",
                table: "Organisations",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "VAT");

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Organisations",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "Pacific/Fiji");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CountryCode",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "FinancialYearEndDay",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "FinancialYearEndMonth",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "TaxLabel",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Organisations");
        }
    }
}
