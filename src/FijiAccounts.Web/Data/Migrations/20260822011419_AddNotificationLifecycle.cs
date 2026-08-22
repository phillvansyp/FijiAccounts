using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AcknowledgedAt",
                table: "Notifications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcknowledgedByUserId",
                table: "Notifications",
                type: "TEXT",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResolvedAt",
                table: "Notifications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolvedByUserId",
                table: "Notifications",
                type: "TEXT",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Notifications",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcknowledgedAt",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "AcknowledgedByUserId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "ResolvedByUserId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Notifications");
        }
    }
}