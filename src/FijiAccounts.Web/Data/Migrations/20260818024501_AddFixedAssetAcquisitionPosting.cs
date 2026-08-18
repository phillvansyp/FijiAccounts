using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFixedAssetAcquisitionPosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AcquisitionBankAccountId",
                table: "FixedAssets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AcquisitionJournalId",
                table: "FixedAssets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_AcquisitionBankAccountId",
                table: "FixedAssets",
                column: "AcquisitionBankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_AcquisitionJournalId",
                table: "FixedAssets",
                column: "AcquisitionJournalId");

            migrationBuilder.AddForeignKey(
                name: "FK_FixedAssets_LedgerAccounts_AcquisitionBankAccountId",
                table: "FixedAssets",
                column: "AcquisitionBankAccountId",
                principalTable: "LedgerAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FixedAssets_PostedJournals_AcquisitionJournalId",
                table: "FixedAssets",
                column: "AcquisitionJournalId",
                principalTable: "PostedJournals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FixedAssets_LedgerAccounts_AcquisitionBankAccountId",
                table: "FixedAssets");

            migrationBuilder.DropForeignKey(
                name: "FK_FixedAssets_PostedJournals_AcquisitionJournalId",
                table: "FixedAssets");

            migrationBuilder.DropIndex(
                name: "IX_FixedAssets_AcquisitionBankAccountId",
                table: "FixedAssets");

            migrationBuilder.DropIndex(
                name: "IX_FixedAssets_AcquisitionJournalId",
                table: "FixedAssets");

            migrationBuilder.DropColumn(
                name: "AcquisitionBankAccountId",
                table: "FixedAssets");

            migrationBuilder.DropColumn(
                name: "AcquisitionJournalId",
                table: "FixedAssets");
        }
    }
}
