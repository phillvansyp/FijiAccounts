using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddImmutableDocumentIntegrityScans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImmutableDocumentIntegrityScans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ObjectCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LinkedDocumentCount = table.Column<int>(type: "INTEGER", nullable: false),
                    VerifiedObjectCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IntegrityFailureCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MissingObjectReferenceCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LegacyDocumentCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UnreferencedObjectCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAtTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImmutableDocumentIntegrityScans", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImmutableDocumentIntegrityScans_OrganisationId_CompletedAtTicks",
                table: "ImmutableDocumentIntegrityScans",
                columns: new[] { "OrganisationId", "CompletedAtTicks" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImmutableDocumentIntegrityScans");
        }
    }
}
