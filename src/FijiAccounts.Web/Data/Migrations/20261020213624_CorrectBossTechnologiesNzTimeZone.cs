using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class CorrectBossTechnologiesNzTimeZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "Organisations"
                SET "TimeZoneId" = 'Pacific/Auckland'
                WHERE "Id" = '19cc2dca-bb60-4430-9b2b-d1a616a78297'
                  AND "CountryCode" = 'NZ'
                  AND "TimeZoneId" = 'Pacific/Fiji';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Previous inconsistent time-zone values cannot be reconstructed safely.
        }
    }
}
