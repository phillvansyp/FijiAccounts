using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddYearEndReviewAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "YearEndReviewAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    YearEndReviewItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    OriginalSize = table.Column<long>(type: "INTEGER", nullable: false),
                    StoredSize = table.Column<long>(type: "INTEGER", nullable: false),
                    IsCompressed = table.Column<bool>(type: "INTEGER", nullable: false),
                    ImmutableDocumentObjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UploadedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YearEndReviewAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_YearEndReviewAttachments_ImmutableDocumentObjects_ImmutableDocumentObjectId",
                        column: x => x.ImmutableDocumentObjectId,
                        principalTable: "ImmutableDocumentObjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YearEndReviewAttachments_YearEndReviewItems_YearEndReviewItemId",
                        column: x => x.YearEndReviewItemId,
                        principalTable: "YearEndReviewItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_YearEndReviewAttachments_ImmutableDocumentObjectId",
                table: "YearEndReviewAttachments",
                column: "ImmutableDocumentObjectId");

            migrationBuilder.CreateIndex(
                name: "IX_YearEndReviewAttachments_OrganisationId_YearEndReviewItemId",
                table: "YearEndReviewAttachments",
                columns: new[] { "OrganisationId", "YearEndReviewItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_YearEndReviewAttachments_YearEndReviewItemId",
                table: "YearEndReviewAttachments",
                column: "YearEndReviewItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "YearEndReviewAttachments");
        }
    }
}
