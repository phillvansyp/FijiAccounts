using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganisationGroupMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganisationGroupMemberships",
                columns: table => new
                {
                    OrganisationGroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganisationGroupMemberships", x => new { x.OrganisationGroupId, x.UserId });
                    table.ForeignKey(
                        name: "FK_OrganisationGroupMemberships_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganisationGroupMemberships_OrganisationGroups_OrganisationGroupId",
                        column: x => x.OrganisationGroupId,
                        principalTable: "OrganisationGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO "OrganisationGroupMemberships"
                    ("OrganisationGroupId", "UserId", "Role", "CreatedAt")
                SELECT
                    o."OrganisationGroupId",
                    m."UserId",
                    MIN(m."Role"),
                    MIN(m."CreatedAt")
                FROM "OrganisationMemberships" m
                INNER JOIN "Organisations" o
                    ON o."Id" = m."OrganisationId"
                WHERE o."OrganisationGroupId" IS NOT NULL
                    AND m."Role" IN (0, 1)
                GROUP BY o."OrganisationGroupId", m."UserId";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_OrganisationGroupMemberships_UserId",
                table: "OrganisationGroupMemberships",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganisationGroupMemberships");
        }
    }
}
