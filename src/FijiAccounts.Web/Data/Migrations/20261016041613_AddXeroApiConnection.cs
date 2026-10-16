using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddXeroApiConnection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "XeroApiSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    XeroMigrationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DatasetKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    RecordCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ImmutableDocumentObjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RetrievedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RetrievedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XeroApiSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_XeroApiSnapshots_ImmutableDocumentObjects_ImmutableDocumentObjectId",
                        column: x => x.ImmutableDocumentObjectId,
                        principalTable: "ImmutableDocumentObjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_XeroApiSnapshots_XeroMigrations_XeroMigrationId",
                        column: x => x.XeroMigrationId,
                        principalTable: "XeroMigrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "XeroConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    XeroConnectionId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    XeroTenantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    XeroTenantName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    TenantCandidatesJson = table.Column<string>(type: "TEXT", nullable: false),
                    ProtectedAccessToken = table.Column<string>(type: "TEXT", nullable: false),
                    ProtectedRefreshToken = table.Column<string>(type: "TEXT", nullable: false),
                    AccessTokenExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    GrantedScopes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConnectedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ConnectedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastSyncError = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XeroConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_XeroConnections_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_XeroApiSnapshots_ImmutableDocumentObjectId",
                table: "XeroApiSnapshots",
                column: "ImmutableDocumentObjectId");

            migrationBuilder.CreateIndex(
                name: "IX_XeroApiSnapshots_XeroMigrationId_DatasetKey_RetrievedAt",
                table: "XeroApiSnapshots",
                columns: new[] { "XeroMigrationId", "DatasetKey", "RetrievedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_XeroConnections_OrganisationId",
                table: "XeroConnections",
                column: "OrganisationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "XeroApiSnapshots");

            migrationBuilder.DropTable(
                name: "XeroConnections");
        }
    }
}
