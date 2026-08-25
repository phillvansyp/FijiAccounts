using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectProgressClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectProgressClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClaimNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ClaimPeriodEnd = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    WorkCompletedAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    RetentionRate = table.Column<decimal>(type: "TEXT", precision: 8, scale: 4, nullable: false),
                    RetentionHeldAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    RetentionReleasedAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    RevenueAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VatTreatment = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    SalesInvoiceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    DecidedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    DecisionReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    InvoicedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectProgressClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectProgressClaims_LedgerAccounts_RevenueAccountId",
                        column: x => x.RevenueAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectProgressClaims_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectProgressClaims_SalesInvoices_SalesInvoiceId",
                        column: x => x.SalesInvoiceId,
                        principalTable: "SalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectProgressClaims_ProjectId_ClaimNumber",
                table: "ProjectProgressClaims",
                columns: new[] { "ProjectId", "ClaimNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectProgressClaims_RevenueAccountId",
                table: "ProjectProgressClaims",
                column: "RevenueAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectProgressClaims_SalesInvoiceId",
                table: "ProjectProgressClaims",
                column: "SalesInvoiceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectProgressClaims");
        }
    }
}
