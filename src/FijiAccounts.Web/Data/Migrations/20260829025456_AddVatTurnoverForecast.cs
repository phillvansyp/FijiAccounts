using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVatTurnoverForecast : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedTaxableTurnoverNext12Months",
                table: "Organisations",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VatTurnoverForecastUpdatedAt",
                table: "Organisations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatTurnoverForecastUpdatedByUserId",
                table: "Organisations",
                type: "TEXT",
                maxLength: 450,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpectedTaxableTurnoverNext12Months",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "VatTurnoverForecastUpdatedAt",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "VatTurnoverForecastUpdatedByUserId",
                table: "Organisations");
        }
    }
}
