using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDimensionAccessGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DimensionAccessMode",
                table: "OrganisationMemberships",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "OrganisationDimensionAccessGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    BranchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DivisionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganisationDimensionAccessGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganisationDimensionAccessGrants_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganisationDimensionAccessGrants_Divisions_DivisionId",
                        column: x => x.DivisionId,
                        principalTable: "Divisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganisationDimensionAccessGrants_OrganisationMemberships_OrganisationId_UserId",
                        columns: x => new { x.OrganisationId, x.UserId },
                        principalTable: "OrganisationMemberships",
                        principalColumns: new[] { "OrganisationId", "UserId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganisationDimensionAccessGrants_BranchId",
                table: "OrganisationDimensionAccessGrants",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganisationDimensionAccessGrants_DivisionId",
                table: "OrganisationDimensionAccessGrants",
                column: "DivisionId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganisationDimensionAccessGrants_OrganisationId_UserId_BranchId_DivisionId",
                table: "OrganisationDimensionAccessGrants",
                columns: new[] { "OrganisationId", "UserId", "BranchId", "DivisionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganisationDimensionAccessGrants");

            migrationBuilder.DropColumn(
                name: "DimensionAccessMode",
                table: "OrganisationMemberships");
        }
    }
}
