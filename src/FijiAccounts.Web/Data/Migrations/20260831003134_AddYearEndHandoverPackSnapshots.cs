using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddYearEndHandoverPackSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "YearEndHandoverPackSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountingPeriodId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false),
                    ImmutableDocumentObjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ContentLength = table.Column<long>(type: "INTEGER", nullable: false),
                    ManifestSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    ReviewApprovalReference = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    ReviewApprovedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReviewApprovedByUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YearEndHandoverPackSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_YearEndHandoverPackSnapshots_AccountingPeriods_AccountingPeriodId",
                        column: x => x.AccountingPeriodId,
                        principalTable: "AccountingPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YearEndHandoverPackSnapshots_ImmutableDocumentObjects_ImmutableDocumentObjectId",
                        column: x => x.ImmutableDocumentObjectId,
                        principalTable: "ImmutableDocumentObjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_YearEndHandoverPackSnapshots_AccountingPeriodId_Version",
                table: "YearEndHandoverPackSnapshots",
                columns: new[] { "AccountingPeriodId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_YearEndHandoverPackSnapshots_ImmutableDocumentObjectId",
                table: "YearEndHandoverPackSnapshots",
                column: "ImmutableDocumentObjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "YearEndHandoverPackSnapshots");
        }
    }
}
