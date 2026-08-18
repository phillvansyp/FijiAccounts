using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Migrations
{
    /// <inheritdoc />
    public partial class ProtectPostedLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PostedJournalLines_LedgerAccounts_LedgerAccountId",
                table: "PostedJournalLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PostedJournals_Organisations_OrganisationId",
                table: "PostedJournals");

            migrationBuilder.AddForeignKey(
                name: "FK_PostedJournalLines_LedgerAccounts_LedgerAccountId",
                table: "PostedJournalLines",
                column: "LedgerAccountId",
                principalTable: "LedgerAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PostedJournals_Organisations_OrganisationId",
                table: "PostedJournals",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PostedJournalLines_LedgerAccounts_LedgerAccountId",
                table: "PostedJournalLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PostedJournals_Organisations_OrganisationId",
                table: "PostedJournals");

            migrationBuilder.AddForeignKey(
                name: "FK_PostedJournalLines_LedgerAccounts_LedgerAccountId",
                table: "PostedJournalLines",
                column: "LedgerAccountId",
                principalTable: "LedgerAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PostedJournals_Organisations_OrganisationId",
                table: "PostedJournals",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
