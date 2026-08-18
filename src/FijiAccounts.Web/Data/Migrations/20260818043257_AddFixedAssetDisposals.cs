using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFixedAssetDisposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FixedAssetDisposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FixedAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisposalDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Proceeds = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    AccumulatedDepreciation = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    BookValue = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    GainLoss = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    BankAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GainAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LossAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PostedJournalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PostedByUserId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FixedAssetDisposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FixedAssetDisposals_FixedAssets_FixedAssetId",
                        column: x => x.FixedAssetId,
                        principalTable: "FixedAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FixedAssetDisposals_LedgerAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FixedAssetDisposals_LedgerAccounts_GainAccountId",
                        column: x => x.GainAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FixedAssetDisposals_LedgerAccounts_LossAccountId",
                        column: x => x.LossAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FixedAssetDisposals_PostedJournals_PostedJournalId",
                        column: x => x.PostedJournalId,
                        principalTable: "PostedJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssetDisposals_BankAccountId",
                table: "FixedAssetDisposals",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssetDisposals_FixedAssetId",
                table: "FixedAssetDisposals",
                column: "FixedAssetId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssetDisposals_GainAccountId",
                table: "FixedAssetDisposals",
                column: "GainAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssetDisposals_LossAccountId",
                table: "FixedAssetDisposals",
                column: "LossAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssetDisposals_PostedJournalId",
                table: "FixedAssetDisposals",
                column: "PostedJournalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FixedAssetDisposals");
        }
    }
}
