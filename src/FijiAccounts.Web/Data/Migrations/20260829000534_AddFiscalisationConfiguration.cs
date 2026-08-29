using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFiscalisationConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FiscalisationConfigurations",
                columns: table => new
                {
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DefaultPaymentType = table.Column<int>(type: "INTEGER", nullable: false),
                    StandardTaxLabel = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    ZeroRatedTaxLabel = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    ExemptTaxLabel = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    OutOfScopeTaxLabel = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalisationConfigurations", x => x.OrganisationId);
                    table.ForeignKey(
                        name: "FK_FiscalisationConfigurations_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FiscalisationConfigurations");
        }
    }
}
