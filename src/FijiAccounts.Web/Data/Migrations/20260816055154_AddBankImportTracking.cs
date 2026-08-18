using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddBankImportTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ImportBatchId",
                table: "BankStatementLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "BankStatementLines",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceHash",
                table: "BankStatementLines",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLines_OrganisationId_BankAccountId_SourceHash",
                table: "BankStatementLines",
                columns: new[] { "OrganisationId", "BankAccountId", "SourceHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BankStatementLines_OrganisationId_BankAccountId_SourceHash",
                table: "BankStatementLines");

            migrationBuilder.DropColumn(
                name: "ImportBatchId",
                table: "BankStatementLines");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "BankStatementLines");

            migrationBuilder.DropColumn(
                name: "SourceHash",
                table: "BankStatementLines");
        }
    }
}
