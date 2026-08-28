using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganisationPermissionProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PermissionProfileId",
                table: "OrganisationMemberships",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrganisationPermissionProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CanManageTeam = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanPostAccounting = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanManageContacts = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanApprovePurchases = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganisationPermissionProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganisationPermissionProfiles_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganisationMemberships_PermissionProfileId",
                table: "OrganisationMemberships",
                column: "PermissionProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganisationPermissionProfiles_OrganisationId_Name",
                table: "OrganisationPermissionProfiles",
                columns: new[] { "OrganisationId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_OrganisationMemberships_OrganisationPermissionProfiles_PermissionProfileId",
                table: "OrganisationMemberships",
                column: "PermissionProfileId",
                principalTable: "OrganisationPermissionProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrganisationMemberships_OrganisationPermissionProfiles_PermissionProfileId",
                table: "OrganisationMemberships");

            migrationBuilder.DropTable(
                name: "OrganisationPermissionProfiles");

            migrationBuilder.DropIndex(
                name: "IX_OrganisationMemberships_PermissionProfileId",
                table: "OrganisationMemberships");

            migrationBuilder.DropColumn(
                name: "PermissionProfileId",
                table: "OrganisationMemberships");
        }
    }
}
