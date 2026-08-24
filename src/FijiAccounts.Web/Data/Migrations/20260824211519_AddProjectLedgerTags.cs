using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectLedgerTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProjectCostCodeId",
                table: "PostedJournalLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "PostedJournalLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PostedJournalLines_ProjectCostCodeId",
                table: "PostedJournalLines",
                column: "ProjectCostCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PostedJournalLines_ProjectId_ProjectCostCodeId",
                table: "PostedJournalLines",
                columns: new[] { "ProjectId", "ProjectCostCodeId" });

            migrationBuilder.AddForeignKey(
                name: "FK_PostedJournalLines_ProjectCostCodes_ProjectCostCodeId",
                table: "PostedJournalLines",
                column: "ProjectCostCodeId",
                principalTable: "ProjectCostCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PostedJournalLines_Projects_ProjectId",
                table: "PostedJournalLines",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PostedJournalLines_ProjectCostCodes_ProjectCostCodeId",
                table: "PostedJournalLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PostedJournalLines_Projects_ProjectId",
                table: "PostedJournalLines");

            migrationBuilder.DropIndex(
                name: "IX_PostedJournalLines_ProjectCostCodeId",
                table: "PostedJournalLines");

            migrationBuilder.DropIndex(
                name: "IX_PostedJournalLines_ProjectId_ProjectCostCodeId",
                table: "PostedJournalLines");

            migrationBuilder.DropColumn(
                name: "ProjectCostCodeId",
                table: "PostedJournalLines");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "PostedJournalLines");
        }
    }
}
