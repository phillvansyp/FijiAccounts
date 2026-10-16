using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddXeroImportControlLayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "XeroMigrationImportRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    XeroMigrationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    IsDryRun = table.Column<bool>(type: "INTEGER", nullable: false),
                    SourceRecordCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ReadyCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ImportedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SkippedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    WarningCount = table.Column<int>(type: "INTEGER", nullable: false),
                    BlockingCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    StartedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XeroMigrationImportRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_XeroMigrationImportRuns_XeroMigrations_XeroMigrationId",
                        column: x => x.XeroMigrationId,
                        principalTable: "XeroMigrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "XeroMigrationMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    XeroMigrationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceKey = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 240, nullable: false),
                    TargetEntityType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    TargetEntityId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TargetDisplayName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XeroMigrationMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_XeroMigrationMappings_XeroMigrations_XeroMigrationId",
                        column: x => x.XeroMigrationId,
                        principalTable: "XeroMigrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "XeroMigrationRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    XeroMigrationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    XeroApiSnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DatasetKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 240, nullable: false),
                    SourceHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetEntityType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    TargetEntityId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReviewMessage = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    PreparedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PreparedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ImportedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XeroMigrationRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_XeroMigrationRecords_XeroApiSnapshots_XeroApiSnapshotId",
                        column: x => x.XeroApiSnapshotId,
                        principalTable: "XeroApiSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_XeroMigrationRecords_XeroMigrations_XeroMigrationId",
                        column: x => x.XeroMigrationId,
                        principalTable: "XeroMigrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "XeroMigrationIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    XeroMigrationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    XeroMigrationRecordId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    IsResolved = table.Column<bool>(type: "INTEGER", nullable: false),
                    Resolution = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ResolvedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XeroMigrationIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_XeroMigrationIssues_XeroMigrationRecords_XeroMigrationRecordId",
                        column: x => x.XeroMigrationRecordId,
                        principalTable: "XeroMigrationRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_XeroMigrationIssues_XeroMigrations_XeroMigrationId",
                        column: x => x.XeroMigrationId,
                        principalTable: "XeroMigrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_XeroMigrationImportRuns_XeroMigrationId_StartedAt",
                table: "XeroMigrationImportRuns",
                columns: new[] { "XeroMigrationId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_XeroMigrationIssues_XeroMigrationId_IsResolved_Severity",
                table: "XeroMigrationIssues",
                columns: new[] { "XeroMigrationId", "IsResolved", "Severity" });

            migrationBuilder.CreateIndex(
                name: "IX_XeroMigrationIssues_XeroMigrationRecordId",
                table: "XeroMigrationIssues",
                column: "XeroMigrationRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_XeroMigrationMappings_XeroMigrationId_Kind_SourceKey",
                table: "XeroMigrationMappings",
                columns: new[] { "XeroMigrationId", "Kind", "SourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_XeroMigrationRecords_OrganisationId_Status",
                table: "XeroMigrationRecords",
                columns: new[] { "OrganisationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_XeroMigrationRecords_XeroApiSnapshotId",
                table: "XeroMigrationRecords",
                column: "XeroApiSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_XeroMigrationRecords_XeroMigrationId_DatasetKey_ExternalId",
                table: "XeroMigrationRecords",
                columns: new[] { "XeroMigrationId", "DatasetKey", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "XeroMigrationImportRuns");

            migrationBuilder.DropTable(
                name: "XeroMigrationIssues");

            migrationBuilder.DropTable(
                name: "XeroMigrationMappings");

            migrationBuilder.DropTable(
                name: "XeroMigrationRecords");
        }
    }
}
