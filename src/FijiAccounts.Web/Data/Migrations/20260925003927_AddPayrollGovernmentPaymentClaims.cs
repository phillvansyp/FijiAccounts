using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollGovernmentPaymentClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PayrollGovernmentPaymentClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankConfirmedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeadlineKey = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PaidOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    RecordedBy = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReopenedInPayrollIsland = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollGovernmentPaymentClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollGovernmentPaymentClaims_PayrollIslandConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "PayrollIslandConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollGovernmentPaymentClaims_ConnectionId_DeadlineKey_PeriodStart",
                table: "PayrollGovernmentPaymentClaims",
                columns: new[] { "ConnectionId", "DeadlineKey", "PeriodStart" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollGovernmentPaymentClaims");
        }
    }
}
