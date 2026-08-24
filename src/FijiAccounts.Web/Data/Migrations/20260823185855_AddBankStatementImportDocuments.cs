using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBankStatementImportDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankStatementImportDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ImportBatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    OriginalSize = table.Column<long>(type: "INTEGER", nullable: false),
                    Content = table.Column<byte[]>(type: "BLOB", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UploadedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementImportDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankStatementImportDocuments_LedgerAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankStatementImportDocuments_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementImportDocuments_BankAccountId",
                table: "BankStatementImportDocuments",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementImportDocuments_OrganisationId_ImportBatchId",
                table: "BankStatementImportDocuments",
                columns: new[] { "OrganisationId", "ImportBatchId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankStatementImportDocuments");
        }
    }
}
