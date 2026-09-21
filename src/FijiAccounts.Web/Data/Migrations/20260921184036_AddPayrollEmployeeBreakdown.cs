using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollEmployeeBreakdown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmployeesJson",
                table: "PayrollIslandPayRunImports",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PayrollBankMatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExternalPaymentId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    PayRunImportId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankStatementLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PostedJournalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    MatchedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollBankMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollBankMatches_BankStatementLines_BankStatementLineId",
                        column: x => x.BankStatementLineId,
                        principalTable: "BankStatementLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollBankMatches_PayrollIslandPayRunImports_PayRunImportId",
                        column: x => x.PayRunImportId,
                        principalTable: "PayrollIslandPayRunImports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBankMatches_BankStatementLineId",
                table: "PayrollBankMatches",
                column: "BankStatementLineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBankMatches_OrganisationId_ConnectionId_ExternalPaymentId",
                table: "PayrollBankMatches",
                columns: new[] { "OrganisationId", "ConnectionId", "ExternalPaymentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBankMatches_PayRunImportId",
                table: "PayrollBankMatches",
                column: "PayRunImportId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollBankMatches");

            migrationBuilder.DropColumn(
                name: "EmployeesJson",
                table: "PayrollIslandPayRunImports");
        }
    }
}
