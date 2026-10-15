using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNzPayInboundIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VehicleRegistration",
                table: "FixedAssets",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NzPayApiCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    TokenSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TokenPrefix = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Scopes = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    RevokedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NzPayApiCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NzPayApiCredentials_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NzPayFbtJournalDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CredentialId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExternalJournalId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    PayloadSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EntryDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    AssetReferencesJson = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PostedJournalId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DecidedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    DecisionReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NzPayFbtJournalDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NzPayFbtJournalDrafts_NzPayApiCredentials_CredentialId",
                        column: x => x.CredentialId,
                        principalTable: "NzPayApiCredentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NzPayFbtJournalDrafts_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NzPayFbtJournalDrafts_PostedJournals_PostedJournalId",
                        column: x => x.PostedJournalId,
                        principalTable: "PostedJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NzPayFbtJournalDraftLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DraftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LedgerAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Debit = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Credit = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NzPayFbtJournalDraftLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NzPayFbtJournalDraftLines_LedgerAccounts_LedgerAccountId",
                        column: x => x.LedgerAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NzPayFbtJournalDraftLines_NzPayFbtJournalDrafts_DraftId",
                        column: x => x.DraftId,
                        principalTable: "NzPayFbtJournalDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NzPayApiCredentials_OrganisationId_TokenSha256",
                table: "NzPayApiCredentials",
                columns: new[] { "OrganisationId", "TokenSha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NzPayFbtJournalDraftLines_DraftId",
                table: "NzPayFbtJournalDraftLines",
                column: "DraftId");

            migrationBuilder.CreateIndex(
                name: "IX_NzPayFbtJournalDraftLines_LedgerAccountId",
                table: "NzPayFbtJournalDraftLines",
                column: "LedgerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_NzPayFbtJournalDrafts_CredentialId",
                table: "NzPayFbtJournalDrafts",
                column: "CredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_NzPayFbtJournalDrafts_OrganisationId_ExternalJournalId",
                table: "NzPayFbtJournalDrafts",
                columns: new[] { "OrganisationId", "ExternalJournalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NzPayFbtJournalDrafts_OrganisationId_IdempotencyKey",
                table: "NzPayFbtJournalDrafts",
                columns: new[] { "OrganisationId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NzPayFbtJournalDrafts_OrganisationId_Status_ReceivedAt",
                table: "NzPayFbtJournalDrafts",
                columns: new[] { "OrganisationId", "Status", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NzPayFbtJournalDrafts_PostedJournalId",
                table: "NzPayFbtJournalDrafts",
                column: "PostedJournalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NzPayFbtJournalDraftLines");

            migrationBuilder.DropTable(
                name: "NzPayFbtJournalDrafts");

            migrationBuilder.DropTable(
                name: "NzPayApiCredentials");

            migrationBuilder.DropColumn(
                name: "VehicleRegistration",
                table: "FixedAssets");
        }
    }
}
