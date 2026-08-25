using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupEliminationJournals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GroupEliminationJournals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationGroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntryDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    PostedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupEliminationJournals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupEliminationJournals_OrganisationGroups_OrganisationGroupId",
                        column: x => x.OrganisationGroupId,
                        principalTable: "OrganisationGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GroupEliminationJournalLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GroupEliminationJournalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AccountName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    AccountType = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Debit = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Credit = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupEliminationJournalLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupEliminationJournalLines_GroupEliminationJournals_GroupEliminationJournalId",
                        column: x => x.GroupEliminationJournalId,
                        principalTable: "GroupEliminationJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroupEliminationJournalLines_GroupEliminationJournalId",
                table: "GroupEliminationJournalLines",
                column: "GroupEliminationJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupEliminationJournals_OrganisationGroupId_EntryDate",
                table: "GroupEliminationJournals",
                columns: new[] { "OrganisationGroupId", "EntryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupEliminationJournals_OrganisationGroupId_Reference",
                table: "GroupEliminationJournals",
                columns: new[] { "OrganisationGroupId", "Reference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupEliminationJournalLines");

            migrationBuilder.DropTable(
                name: "GroupEliminationJournals");
        }
    }
}
