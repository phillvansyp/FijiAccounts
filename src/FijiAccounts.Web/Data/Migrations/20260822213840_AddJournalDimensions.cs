using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJournalDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "PostedJournalLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DivisionId",
                table: "PostedJournalLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "PostedJournalLines"
                SET "BranchId" = (
                    SELECT b."Id"
                    FROM "PostedJournals" j
                    INNER JOIN "Branches" b
                        ON b."OrganisationId" = j."OrganisationId"
                    WHERE j."Id" = "PostedJournalLines"."PostedJournalId"
                        AND b."IsDefault" = 1
                    LIMIT 1
                );

                UPDATE "PostedJournalLines"
                SET "DivisionId" = (
                    SELECT d."Id"
                    FROM "Divisions" d
                    WHERE d."BranchId" = "PostedJournalLines"."BranchId"
                        AND d."IsDefault" = 1
                    LIMIT 1
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_PostedJournalLines_BranchId_DivisionId",
                table: "PostedJournalLines",
                columns: new[] { "BranchId", "DivisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_PostedJournalLines_DivisionId",
                table: "PostedJournalLines",
                column: "DivisionId");

            migrationBuilder.AddForeignKey(
                name: "FK_PostedJournalLines_Branches_BranchId",
                table: "PostedJournalLines",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PostedJournalLines_Divisions_DivisionId",
                table: "PostedJournalLines",
                column: "DivisionId",
                principalTable: "Divisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PostedJournalLines_Branches_BranchId",
                table: "PostedJournalLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PostedJournalLines_Divisions_DivisionId",
                table: "PostedJournalLines");

            migrationBuilder.DropIndex(
                name: "IX_PostedJournalLines_BranchId_DivisionId",
                table: "PostedJournalLines");

            migrationBuilder.DropIndex(
                name: "IX_PostedJournalLines_DivisionId",
                table: "PostedJournalLines");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "PostedJournalLines");

            migrationBuilder.DropColumn(
                name: "DivisionId",
                table: "PostedJournalLines");
        }
    }
}
