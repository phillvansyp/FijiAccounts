using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectWipPosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProjectContractAssetAccountId",
                table: "Organisations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectContractLiabilityAccountId",
                table: "Organisations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectRevenueRecognitionAccountId",
                table: "Organisations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProjectWipPostings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AsAt = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PreviousWipAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    RequiredWipAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    MovementAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    PostedJournalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PostedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectWipPostings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectWipPostings_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectWipPostings_PostedJournals_PostedJournalId",
                        column: x => x.PostedJournalId,
                        principalTable: "PostedJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectWipPostings_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Organisations_ProjectContractAssetAccountId",
                table: "Organisations",
                column: "ProjectContractAssetAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Organisations_ProjectContractLiabilityAccountId",
                table: "Organisations",
                column: "ProjectContractLiabilityAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Organisations_ProjectRevenueRecognitionAccountId",
                table: "Organisations",
                column: "ProjectRevenueRecognitionAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectWipPostings_OrganisationId",
                table: "ProjectWipPostings",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectWipPostings_PostedJournalId",
                table: "ProjectWipPostings",
                column: "PostedJournalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectWipPostings_ProjectId_AsAt",
                table: "ProjectWipPostings",
                columns: new[] { "ProjectId", "AsAt" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Organisations_LedgerAccounts_ProjectContractAssetAccountId",
                table: "Organisations",
                column: "ProjectContractAssetAccountId",
                principalTable: "LedgerAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Organisations_LedgerAccounts_ProjectContractLiabilityAccountId",
                table: "Organisations",
                column: "ProjectContractLiabilityAccountId",
                principalTable: "LedgerAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Organisations_LedgerAccounts_ProjectRevenueRecognitionAccountId",
                table: "Organisations",
                column: "ProjectRevenueRecognitionAccountId",
                principalTable: "LedgerAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Organisations_LedgerAccounts_ProjectContractAssetAccountId",
                table: "Organisations");

            migrationBuilder.DropForeignKey(
                name: "FK_Organisations_LedgerAccounts_ProjectContractLiabilityAccountId",
                table: "Organisations");

            migrationBuilder.DropForeignKey(
                name: "FK_Organisations_LedgerAccounts_ProjectRevenueRecognitionAccountId",
                table: "Organisations");

            migrationBuilder.DropTable(
                name: "ProjectWipPostings");

            migrationBuilder.DropIndex(
                name: "IX_Organisations_ProjectContractAssetAccountId",
                table: "Organisations");

            migrationBuilder.DropIndex(
                name: "IX_Organisations_ProjectContractLiabilityAccountId",
                table: "Organisations");

            migrationBuilder.DropIndex(
                name: "IX_Organisations_ProjectRevenueRecognitionAccountId",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "ProjectContractAssetAccountId",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "ProjectContractLiabilityAccountId",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "ProjectRevenueRecognitionAccountId",
                table: "Organisations");
        }
    }
}
