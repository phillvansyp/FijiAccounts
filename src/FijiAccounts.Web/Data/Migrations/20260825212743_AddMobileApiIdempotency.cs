using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMobileApiIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CreatedAtTicks",
                table: "Notifications",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql(
                """
                UPDATE Notifications
                SET CreatedAtTicks = CAST(
                    (julianday(CreatedAt) - 1721425.5) * 864000000000
                    AS INTEGER);
                """);

            migrationBuilder.CreateTable(
                name: "MobileIdempotencyRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Operation = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    RequestHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StatusCode = table.Column<int>(type: "INTEGER", nullable: false),
                    ResultCode = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobileIdempotencyRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_OrganisationId_IsRead_CreatedAtTicks_Id",
                table: "Notifications",
                columns: new[] { "OrganisationId", "IsRead", "CreatedAtTicks", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MobileIdempotencyRecords_ExpiresAt",
                table: "MobileIdempotencyRecords",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_MobileIdempotencyRecords_OrganisationId_UserId_Key",
                table: "MobileIdempotencyRecords",
                columns: new[] { "OrganisationId", "UserId", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MobileIdempotencyRecords");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_OrganisationId_IsRead_CreatedAtTicks_Id",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "CreatedAtTicks",
                table: "Notifications");
        }
    }
}
