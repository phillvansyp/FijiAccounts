using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddXeroMigrationWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "XeroMigrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceOrganisationName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    CutoverDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    IncludeFullHistory = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    XeroEntryStopped = table.Column<bool>(type: "INTEGER", nullable: false),
                    TrialBalanceReconciled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReceivablesReconciled = table.Column<bool>(type: "INTEGER", nullable: false),
                    PayablesReconciled = table.Column<bool>(type: "INTEGER", nullable: false),
                    BankReconciled = table.Column<bool>(type: "INTEGER", nullable: false),
                    FixedAssetsReconciled = table.Column<bool>(type: "INTEGER", nullable: false),
                    GstReconciled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ManagementApproved = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XeroMigrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_XeroMigrations_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "XeroMigrationFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    XeroMigrationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ImmutableDocumentObjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UploadedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XeroMigrationFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_XeroMigrationFiles_ImmutableDocumentObjects_ImmutableDocumentObjectId",
                        column: x => x.ImmutableDocumentObjectId,
                        principalTable: "ImmutableDocumentObjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_XeroMigrationFiles_XeroMigrations_XeroMigrationId",
                        column: x => x.XeroMigrationId,
                        principalTable: "XeroMigrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_XeroMigrationFiles_ImmutableDocumentObjectId",
                table: "XeroMigrationFiles",
                column: "ImmutableDocumentObjectId");

            migrationBuilder.CreateIndex(
                name: "IX_XeroMigrationFiles_XeroMigrationId_Category",
                table: "XeroMigrationFiles",
                columns: new[] { "XeroMigrationId", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_XeroMigrations_OrganisationId",
                table: "XeroMigrations",
                column: "OrganisationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "XeroMigrationFiles");

            migrationBuilder.DropTable(
                name: "XeroMigrations");
        }
    }
}
