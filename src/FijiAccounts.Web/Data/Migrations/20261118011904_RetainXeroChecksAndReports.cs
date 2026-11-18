using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RetainXeroChecksAndReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "XeroDatasetChecks",
                columns: table => new
                {
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Dataset = table.Column<string>(type: "TEXT", nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XeroDatasetChecks", x => new { x.ConnectionId, x.TenantId, x.Dataset });
                });

            migrationBuilder.CreateTable(
                name: "XeroPeriodReports",
                columns: table => new
                {
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    PeriodFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PeriodTo = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    RetrievedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Payload = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XeroPeriodReports", x => new { x.ConnectionId, x.TenantId, x.Kind, x.PeriodFrom, x.PeriodTo });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "XeroDatasetChecks");

            migrationBuilder.DropTable(
                name: "XeroPeriodReports");
        }
    }
}
