using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBankReconciliationSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankReconciliationSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StatementStartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    StatementEndDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    OpeningStatementBalance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ClosingStatementBalance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    LedgerBalance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Difference = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    IsCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CompletedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankReconciliationSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankReconciliationSessions_LedgerAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankReconciliationSessions_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationSessions_BankAccountId",
                table: "BankReconciliationSessions",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationSessions_OrganisationId_BankAccountId_StatementEndDate",
                table: "BankReconciliationSessions",
                columns: new[] { "OrganisationId", "BankAccountId", "StatementEndDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankReconciliationSessions");
        }
    }
}
