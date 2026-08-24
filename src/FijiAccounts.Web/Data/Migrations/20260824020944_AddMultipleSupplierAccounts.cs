using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMultipleSupplierAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupplierAccountProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SupplierId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    AccountNumber = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierAccountProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierAccountProfiles_BusinessParties_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "BusinessParties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SupplierAccountProfiles_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierAccountProfiles_OrganisationId_SupplierId_AccountNumber",
                table: "SupplierAccountProfiles",
                columns: new[] { "OrganisationId", "SupplierId", "AccountNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierAccountProfiles_SupplierId",
                table: "SupplierAccountProfiles",
                column: "SupplierId");

            migrationBuilder.Sql(
                """
                INSERT INTO SupplierAccountProfiles
                    (Id, OrganisationId, SupplierId, Label, AccountNumber, IsDefault, IsActive)
                SELECT
                    lower(hex(randomblob(16))),
                    OrganisationId,
                    Id,
                    'Primary',
                    trim(SupplierAccountNumber),
                    1,
                    1
                FROM BusinessParties
                WHERE SupplierAccountNumber IS NOT NULL
                  AND trim(SupplierAccountNumber) <> '';
                """);

            migrationBuilder.DropColumn(
                name: "SupplierAccountNumber",
                table: "BusinessParties");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SupplierAccountNumber",
                table: "BusinessParties",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE BusinessParties
                SET SupplierAccountNumber =
                    (SELECT AccountNumber
                     FROM SupplierAccountProfiles
                     WHERE SupplierId = BusinessParties.Id
                     ORDER BY IsDefault DESC, Label
                     LIMIT 1);
                """);

            migrationBuilder.DropTable(
                name: "SupplierAccountProfiles");
        }
    }
}
