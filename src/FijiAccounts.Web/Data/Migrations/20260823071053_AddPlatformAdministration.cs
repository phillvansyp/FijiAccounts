using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InternalNotes",
                table: "OrganisationGroups",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDemo",
                table: "OrganisationGroups",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "OrganisationGroups",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SuspendedAt",
                table: "OrganisationGroups",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlatformAuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AdministratorUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    OrganisationGroupId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    JsonData = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformAuditEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAuditEvents_AdministratorUserId",
                table: "PlatformAuditEvents",
                column: "AdministratorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAuditEvents_OrganisationGroupId_OccurredAt",
                table: "PlatformAuditEvents",
                columns: new[] { "OrganisationGroupId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformAuditEvents");

            migrationBuilder.DropColumn(
                name: "InternalNotes",
                table: "OrganisationGroups");

            migrationBuilder.DropColumn(
                name: "IsDemo",
                table: "OrganisationGroups");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "OrganisationGroups");

            migrationBuilder.DropColumn(
                name: "SuspendedAt",
                table: "OrganisationGroups");
        }
    }
}
