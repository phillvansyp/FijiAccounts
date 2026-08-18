using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Organisations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LegalName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    TradingName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Tin = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    BaseCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organisations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccountantEngagements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PracticeOrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientOrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Access = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountantEngagements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountantEngagements_Organisations_ClientOrganisationId",
                        column: x => x.ClientOrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountantEngagements_Organisations_PracticeOrganisationId",
                        column: x => x.PracticeOrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrganisationMemberships",
                columns: table => new
                {
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganisationMemberships", x => new { x.OrganisationId, x.UserId });
                    table.ForeignKey(
                        name: "FK_OrganisationMemberships_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganisationMemberships_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountantEngagements_ClientOrganisationId",
                table: "AccountantEngagements",
                column: "ClientOrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountantEngagements_PracticeOrganisationId_ClientOrganisationId",
                table: "AccountantEngagements",
                columns: new[] { "PracticeOrganisationId", "ClientOrganisationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganisationMemberships_UserId",
                table: "OrganisationMemberships",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountantEngagements");

            migrationBuilder.DropTable(
                name: "OrganisationMemberships");

            migrationBuilder.DropTable(
                name: "Organisations");
        }
    }
}
