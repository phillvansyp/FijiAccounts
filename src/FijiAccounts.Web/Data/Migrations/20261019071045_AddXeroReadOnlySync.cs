using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddXeroReadOnlySync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConsecutiveSyncFailures",
                table: "XeroConnections",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ContinuousSyncEnabled",
                table: "XeroConnections",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastFullReconciliationAt",
                table: "XeroConnections",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextSyncAt",
                table: "XeroConnections",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SyncLeaseUntil",
                table: "XeroConnections",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "XeroMirrorEntityLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    XeroConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DatasetKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    TargetEntityType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    TargetEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LinkedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XeroMirrorEntityLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_XeroMirrorEntityLinks_XeroConnections_XeroConnectionId",
                        column: x => x.XeroConnectionId,
                        principalTable: "XeroConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "XeroMirrorRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    XeroConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DatasetKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 240, nullable: false),
                    SourceHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceJson = table.Column<string>(type: "TEXT", nullable: false),
                    SourceUpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XeroMirrorRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_XeroMirrorRecords_XeroConnections_XeroConnectionId",
                        column: x => x.XeroConnectionId,
                        principalTable: "XeroConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "XeroReadOnlySyncRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    XeroConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    IsFullReconciliation = table.Column<bool>(type: "INTEGER", nullable: false),
                    DatasetCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RecordCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ChangedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ArchivedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorSummary = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XeroReadOnlySyncRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_XeroReadOnlySyncRuns_XeroConnections_XeroConnectionId",
                        column: x => x.XeroConnectionId,
                        principalTable: "XeroConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_XeroMirrorEntityLinks_XeroConnectionId_DatasetKey_ExternalId",
                table: "XeroMirrorEntityLinks",
                columns: new[] { "XeroConnectionId", "DatasetKey", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_XeroMirrorRecords_OrganisationId_DatasetKey_IsArchived",
                table: "XeroMirrorRecords",
                columns: new[] { "OrganisationId", "DatasetKey", "IsArchived" });

            migrationBuilder.CreateIndex(
                name: "IX_XeroMirrorRecords_XeroConnectionId_DatasetKey_ExternalId",
                table: "XeroMirrorRecords",
                columns: new[] { "XeroConnectionId", "DatasetKey", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_XeroReadOnlySyncRuns_XeroConnectionId_StartedAt",
                table: "XeroReadOnlySyncRuns",
                columns: new[] { "XeroConnectionId", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "XeroMirrorEntityLinks");

            migrationBuilder.DropTable(
                name: "XeroMirrorRecords");

            migrationBuilder.DropTable(
                name: "XeroReadOnlySyncRuns");

            migrationBuilder.DropColumn(
                name: "ConsecutiveSyncFailures",
                table: "XeroConnections");

            migrationBuilder.DropColumn(
                name: "ContinuousSyncEnabled",
                table: "XeroConnections");

            migrationBuilder.DropColumn(
                name: "LastFullReconciliationAt",
                table: "XeroConnections");

            migrationBuilder.DropColumn(
                name: "NextSyncAt",
                table: "XeroConnections");

            migrationBuilder.DropColumn(
                name: "SyncLeaseUntil",
                table: "XeroConnections");
        }
    }
}
