using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddBankReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankStatementLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    MatchedPostedJournalLineId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReconciledAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReconciledByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    ImportedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankStatementLines_LedgerAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankStatementLines_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankStatementLines_PostedJournalLines_MatchedPostedJournalLineId",
                        column: x => x.MatchedPostedJournalLineId,
                        principalTable: "PostedJournalLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLines_BankAccountId",
                table: "BankStatementLines",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLines_MatchedPostedJournalLineId",
                table: "BankStatementLines",
                column: "MatchedPostedJournalLineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLines_OrganisationId_BankAccountId_TransactionDate",
                table: "BankStatementLines",
                columns: new[] { "OrganisationId", "BankAccountId", "TransactionDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankStatementLines");
        }
    }
}
