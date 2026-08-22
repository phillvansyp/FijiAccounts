using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupExchangeRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PresentationCurrency",
                table: "OrganisationGroups",
                type: "TEXT",
                maxLength: 3,
                nullable: false,
                defaultValue: "FJD");

            migrationBuilder.Sql(
                """
                UPDATE "OrganisationGroups"
                SET "PresentationCurrency" = COALESCE(
                    (
                        SELECT company."BaseCurrency"
                        FROM "Organisations" AS company
                        WHERE company."OrganisationGroupId" = "OrganisationGroups"."Id"
                        ORDER BY company."CreatedAt", company."Id"
                        LIMIT 1
                    ),
                    'FJD');
                """);

            migrationBuilder.CreateTable(
                name: "GroupExchangeRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationGroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    ToCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Rate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 8, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupExchangeRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupExchangeRates_OrganisationGroups_OrganisationGroupId",
                        column: x => x.OrganisationGroupId,
                        principalTable: "OrganisationGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroupExchangeRates_OrganisationGroupId_FromCurrency_ToCurrency_Type_EffectiveDate",
                table: "GroupExchangeRates",
                columns: new[] { "OrganisationGroupId", "FromCurrency", "ToCurrency", "Type", "EffectiveDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupExchangeRates");

            migrationBuilder.DropColumn(
                name: "PresentationCurrency",
                table: "OrganisationGroups");
        }
    }
}
