using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringInvoiceAutomationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RecurringInvoiceAutomationEnabled",
                table: "Organisations",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "RecurringInvoiceAutomationTime",
                table: "Organisations",
                type: "TEXT",
                nullable: false,
                defaultValue: new TimeOnly(6, 0, 0));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecurringInvoiceAutomationEnabled",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "RecurringInvoiceAutomationTime",
                table: "Organisations");
        }
    }
}
