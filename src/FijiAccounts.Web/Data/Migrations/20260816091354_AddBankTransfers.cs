using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddBankTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromBankAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToBankAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TransferDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    PostedJournalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankTransfers_LedgerAccounts_FromBankAccountId",
                        column: x => x.FromBankAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankTransfers_LedgerAccounts_ToBankAccountId",
                        column: x => x.ToBankAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankTransfers_PostedJournals_PostedJournalId",
                        column: x => x.PostedJournalId,
                        principalTable: "PostedJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankTransfers_FromBankAccountId",
                table: "BankTransfers",
                column: "FromBankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransfers_OrganisationId_Reference",
                table: "BankTransfers",
                columns: new[] { "OrganisationId", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankTransfers_PostedJournalId",
                table: "BankTransfers",
                column: "PostedJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransfers_ToBankAccountId",
                table: "BankTransfers",
                column: "ToBankAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankTransfers");
        }
    }
}
