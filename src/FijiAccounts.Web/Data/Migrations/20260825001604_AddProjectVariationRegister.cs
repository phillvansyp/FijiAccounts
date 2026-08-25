using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectVariationRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ApprovedVariationValue",
                table: "Projects",
                newName: "OpeningApprovedVariationValue");

            migrationBuilder.CreateTable(
                name: "ProjectVariations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VariationNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    RequestedDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    DecidedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    DecisionReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectVariations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectVariations_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectVariations_ProjectId_VariationNumber",
                table: "ProjectVariations",
                columns: new[] { "ProjectId", "VariationNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectVariations");

            migrationBuilder.RenameColumn(
                name: "OpeningApprovedVariationValue",
                table: "Projects",
                newName: "ApprovedVariationValue");
        }
    }
}
