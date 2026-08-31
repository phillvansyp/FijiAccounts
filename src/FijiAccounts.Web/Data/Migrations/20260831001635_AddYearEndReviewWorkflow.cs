using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddYearEndReviewWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "YearEndReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountingPeriodId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    StartedByUserId = table.Column<string>(type: "TEXT", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ApprovedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    ApprovalReference = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YearEndReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_YearEndReviews_AccountingPeriods_AccountingPeriodId",
                        column: x => x.AccountingPeriodId,
                        principalTable: "AccountingPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "YearEndReviewItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    YearEndReviewId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Area = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReviewedByUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YearEndReviewItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_YearEndReviewItems_YearEndReviews_YearEndReviewId",
                        column: x => x.YearEndReviewId,
                        principalTable: "YearEndReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_YearEndReviewItems_YearEndReviewId_Area",
                table: "YearEndReviewItems",
                columns: new[] { "YearEndReviewId", "Area" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_YearEndReviews_AccountingPeriodId",
                table: "YearEndReviews",
                column: "AccountingPeriodId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "YearEndReviewItems");

            migrationBuilder.DropTable(
                name: "YearEndReviews");
        }
    }
}
