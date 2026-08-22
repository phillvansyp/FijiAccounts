using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEnterpriseStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationGroupId",
                table: "Organisations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Branches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Branches_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrganisationGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganisationGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Divisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BranchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Divisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Divisions_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    });

            migrationBuilder.Sql(
                """
                INSERT INTO "OrganisationGroups" ("Id", "Name", "CreatedAt")
                SELECT "Id", "LegalName" || ' Group', "CreatedAt"
                FROM "Organisations";

                UPDATE "Organisations"
                SET "OrganisationGroupId" = "Id";

                INSERT INTO "Branches"
                    ("Id", "OrganisationId", "Code", "Name", "IsDefault", "IsActive", "CreatedAt")
                SELECT
                    unit."Id",
                    unit."OrganisationId",
                    unit."Code",
                    unit."Name",
                    CASE WHEN unit."Id" = (
                        SELECT firstBranch."Id"
                        FROM "OrganisationUnits" AS firstBranch
                        WHERE firstBranch."OrganisationId" = unit."OrganisationId"
                            AND firstBranch."Type" = 1
                        ORDER BY firstBranch."CreatedAt", firstBranch."Id"
                        LIMIT 1
                    ) THEN 1 ELSE 0 END,
                    unit."IsActive",
                    unit."CreatedAt"
                FROM "OrganisationUnits" AS unit
                WHERE unit."Type" = 1;

                INSERT INTO "Branches"
                    ("Id", "OrganisationId", "Code", "Name", "IsDefault", "IsActive", "CreatedAt")
                SELECT
                    organisation."Id",
                    organisation."Id",
                    'MAIN',
                    'Main Branch',
                    1,
                    1,
                    organisation."CreatedAt"
                FROM "Organisations" AS organisation
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM "Branches" AS branch
                    WHERE branch."OrganisationId" = organisation."Id"
                );

                INSERT INTO "Divisions"
                    ("Id", "BranchId", "Code", "Name", "IsDefault", "IsActive", "CreatedAt")
                SELECT
                    unit."Id",
                    (
                        SELECT branch."Id"
                        FROM "Branches" AS branch
                        WHERE branch."OrganisationId" = unit."OrganisationId"
                            AND branch."IsDefault" = 1
                        LIMIT 1
                    ),
                    unit."Code",
                    unit."Name",
                    CASE WHEN unit."Id" = (
                        SELECT firstDivision."Id"
                        FROM "OrganisationUnits" AS firstDivision
                        WHERE firstDivision."OrganisationId" = unit."OrganisationId"
                            AND firstDivision."Type" = 0
                        ORDER BY firstDivision."CreatedAt", firstDivision."Id"
                        LIMIT 1
                    ) THEN 1 ELSE 0 END,
                    unit."IsActive",
                    unit."CreatedAt"
                FROM "OrganisationUnits" AS unit
                WHERE unit."Type" = 0;

                INSERT INTO "Divisions"
                    ("Id", "BranchId", "Code", "Name", "IsDefault", "IsActive", "CreatedAt")
                SELECT
                    branch."Id",
                    branch."Id",
                    'GENERAL',
                    'General',
                    1,
                    1,
                    branch."CreatedAt"
                FROM "Branches" AS branch
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM "Divisions" AS division
                    WHERE division."BranchId" = branch."Id"
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Organisations_OrganisationGroupId",
                table: "Organisations",
                column: "OrganisationGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Branches_OrganisationId_Code",
                table: "Branches",
                columns: new[] { "OrganisationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Branches_OrganisationId_Name",
                table: "Branches",
                columns: new[] { "OrganisationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Divisions_BranchId_Code",
                table: "Divisions",
                columns: new[] { "BranchId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Divisions_BranchId_Name",
                table: "Divisions",
                columns: new[] { "BranchId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Organisations_OrganisationGroups_OrganisationGroupId",
                table: "Organisations",
                column: "OrganisationGroupId",
                principalTable: "OrganisationGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Organisations_OrganisationGroups_OrganisationGroupId",
                table: "Organisations");

            migrationBuilder.DropTable(
                name: "Divisions");

            migrationBuilder.DropTable(
                name: "OrganisationGroups");

            migrationBuilder.DropTable(
                name: "Branches");

            migrationBuilder.DropIndex(
                name: "IX_Organisations_OrganisationGroupId",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "OrganisationGroupId",
                table: "Organisations");
        }
    }
}
