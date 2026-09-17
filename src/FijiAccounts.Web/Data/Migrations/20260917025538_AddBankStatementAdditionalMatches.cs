using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBankStatementAdditionalMatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankStatementAdditionalMatches",
                columns: table => new
                {
                    PostedJournalLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankStatementLineId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementAdditionalMatches", x => x.PostedJournalLineId);
                    table.ForeignKey(
                        name: "FK_BankStatementAdditionalMatches_BankStatementLines_BankStatementLineId",
                        column: x => x.BankStatementLineId,
                        principalTable: "BankStatementLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BankStatementAdditionalMatches_PostedJournalLines_PostedJournalLineId",
                        column: x => x.PostedJournalLineId,
                        principalTable: "PostedJournalLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementAdditionalMatches_BankStatementLineId",
                table: "BankStatementAdditionalMatches",
                column: "BankStatementLineId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankStatementAdditionalMatches");
        }
    }
}
