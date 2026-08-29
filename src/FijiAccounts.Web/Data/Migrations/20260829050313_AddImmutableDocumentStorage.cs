using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddImmutableDocumentStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ImmutableDocumentObjectId",
                table: "SupplierBillAttachments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImmutableDocumentObjectId",
                table: "BusinessPartyDocuments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImmutableDocumentObjectId",
                table: "BankStatementImportDocuments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ImmutableDocumentObjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ObjectKey = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ContentLength = table.Column<long>(type: "INTEGER", nullable: false),
                    Content = table.Column<byte[]>(type: "BLOB", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImmutableDocumentObjects", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBillAttachments_ImmutableDocumentObjectId",
                table: "SupplierBillAttachments",
                column: "ImmutableDocumentObjectId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessPartyDocuments_ImmutableDocumentObjectId",
                table: "BusinessPartyDocuments",
                column: "ImmutableDocumentObjectId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementImportDocuments_ImmutableDocumentObjectId",
                table: "BankStatementImportDocuments",
                column: "ImmutableDocumentObjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ImmutableDocumentObjects_OrganisationId_Provider_ObjectKey",
                table: "ImmutableDocumentObjects",
                columns: new[] { "OrganisationId", "Provider", "ObjectKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImmutableDocumentObjects_OrganisationId_Sha256",
                table: "ImmutableDocumentObjects",
                columns: new[] { "OrganisationId", "Sha256" });

            migrationBuilder.AddForeignKey(
                name: "FK_BankStatementImportDocuments_ImmutableDocumentObjects_ImmutableDocumentObjectId",
                table: "BankStatementImportDocuments",
                column: "ImmutableDocumentObjectId",
                principalTable: "ImmutableDocumentObjects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessPartyDocuments_ImmutableDocumentObjects_ImmutableDocumentObjectId",
                table: "BusinessPartyDocuments",
                column: "ImmutableDocumentObjectId",
                principalTable: "ImmutableDocumentObjects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SupplierBillAttachments_ImmutableDocumentObjects_ImmutableDocumentObjectId",
                table: "SupplierBillAttachments",
                column: "ImmutableDocumentObjectId",
                principalTable: "ImmutableDocumentObjects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BankStatementImportDocuments_ImmutableDocumentObjects_ImmutableDocumentObjectId",
                table: "BankStatementImportDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_BusinessPartyDocuments_ImmutableDocumentObjects_ImmutableDocumentObjectId",
                table: "BusinessPartyDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_SupplierBillAttachments_ImmutableDocumentObjects_ImmutableDocumentObjectId",
                table: "SupplierBillAttachments");

            migrationBuilder.DropTable(
                name: "ImmutableDocumentObjects");

            migrationBuilder.DropIndex(
                name: "IX_SupplierBillAttachments_ImmutableDocumentObjectId",
                table: "SupplierBillAttachments");

            migrationBuilder.DropIndex(
                name: "IX_BusinessPartyDocuments_ImmutableDocumentObjectId",
                table: "BusinessPartyDocuments");

            migrationBuilder.DropIndex(
                name: "IX_BankStatementImportDocuments_ImmutableDocumentObjectId",
                table: "BankStatementImportDocuments");

            migrationBuilder.DropColumn(
                name: "ImmutableDocumentObjectId",
                table: "SupplierBillAttachments");

            migrationBuilder.DropColumn(
                name: "ImmutableDocumentObjectId",
                table: "BusinessPartyDocuments");

            migrationBuilder.DropColumn(
                name: "ImmutableDocumentObjectId",
                table: "BankStatementImportDocuments");
        }
    }
}
