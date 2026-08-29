using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIntercompanyReconciliationWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IntercompanyTransactionTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationGroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CounterpartyOrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentType = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntercompanyTransactionTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntercompanyTransactionTags_OrganisationGroups_OrganisationGroupId",
                        column: x => x.OrganisationGroupId,
                        principalTable: "OrganisationGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IntercompanyTransactionTags_Organisations_CounterpartyOrganisationId",
                        column: x => x.CounterpartyOrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IntercompanyTransactionTags_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IntercompanyTransactionMatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationGroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LeftTransactionTagId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RightTransactionTagId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    AmountDifference = table.Column<decimal>(type: "TEXT", nullable: false),
                    HasCurrencyMismatch = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProposedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ProposedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReviewedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    GroupEliminationJournalId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntercompanyTransactionMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntercompanyTransactionMatches_GroupEliminationJournals_GroupEliminationJournalId",
                        column: x => x.GroupEliminationJournalId,
                        principalTable: "GroupEliminationJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IntercompanyTransactionMatches_IntercompanyTransactionTags_LeftTransactionTagId",
                        column: x => x.LeftTransactionTagId,
                        principalTable: "IntercompanyTransactionTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IntercompanyTransactionMatches_IntercompanyTransactionTags_RightTransactionTagId",
                        column: x => x.RightTransactionTagId,
                        principalTable: "IntercompanyTransactionTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IntercompanyTransactionMatches_OrganisationGroups_OrganisationGroupId",
                        column: x => x.OrganisationGroupId,
                        principalTable: "OrganisationGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IntercompanyTransactionMatches_GroupEliminationJournalId",
                table: "IntercompanyTransactionMatches",
                column: "GroupEliminationJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_IntercompanyTransactionMatches_LeftTransactionTagId_RightTransactionTagId",
                table: "IntercompanyTransactionMatches",
                columns: new[] { "LeftTransactionTagId", "RightTransactionTagId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntercompanyTransactionMatches_OrganisationGroupId_Status",
                table: "IntercompanyTransactionMatches",
                columns: new[] { "OrganisationGroupId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_IntercompanyTransactionMatches_RightTransactionTagId",
                table: "IntercompanyTransactionMatches",
                column: "RightTransactionTagId");

            migrationBuilder.CreateIndex(
                name: "IX_IntercompanyTransactionTags_CounterpartyOrganisationId",
                table: "IntercompanyTransactionTags",
                column: "CounterpartyOrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_IntercompanyTransactionTags_DocumentType_SourceDocumentId",
                table: "IntercompanyTransactionTags",
                columns: new[] { "DocumentType", "SourceDocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntercompanyTransactionTags_OrganisationGroupId_OrganisationId_CounterpartyOrganisationId",
                table: "IntercompanyTransactionTags",
                columns: new[] { "OrganisationGroupId", "OrganisationId", "CounterpartyOrganisationId" });

            migrationBuilder.CreateIndex(
                name: "IX_IntercompanyTransactionTags_OrganisationId",
                table: "IntercompanyTransactionTags",
                column: "OrganisationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IntercompanyTransactionMatches");

            migrationBuilder.DropTable(
                name: "IntercompanyTransactionTags");
        }
    }
}
