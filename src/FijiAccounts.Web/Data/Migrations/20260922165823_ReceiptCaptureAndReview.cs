using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReceiptCaptureAndReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployeeReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmittedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    RequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Merchant = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ReceiptDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    PaidPersonally = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ReviewNote = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ReviewedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeReceipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReceiptContributors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptContributors", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeReceipts_OrganisationId_SubmittedByUserId_RequestId",
                table: "EmployeeReceipts",
                columns: new[] { "OrganisationId", "SubmittedByUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptContributors_OrganisationId_UserId",
                table: "ReceiptContributors",
                columns: new[] { "OrganisationId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeReceipts");

            migrationBuilder.DropTable(
                name: "ReceiptContributors");
        }
    }
}
