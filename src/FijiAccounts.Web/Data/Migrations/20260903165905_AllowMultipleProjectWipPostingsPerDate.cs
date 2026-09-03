using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AllowMultipleProjectWipPostingsPerDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProjectWipPostings_ProjectId_AsAt",
                table: "ProjectWipPostings");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectWipPostings_ProjectId_AsAt",
                table: "ProjectWipPostings",
                columns: new[] { "ProjectId", "AsAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProjectWipPostings_ProjectId_AsAt",
                table: "ProjectWipPostings");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectWipPostings_ProjectId_AsAt",
                table: "ProjectWipPostings",
                columns: new[] { "ProjectId", "AsAt" },
                unique: true);
        }
    }
}
