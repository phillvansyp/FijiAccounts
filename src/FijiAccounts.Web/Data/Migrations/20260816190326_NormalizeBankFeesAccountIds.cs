using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeBankFeesAccountIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQLite stores Guid values as uppercase text through EF Core. The
            // original raw-SQL seed used lowercase text, making the account
            // visible by code but impossible to resolve later by its Guid key.
            migrationBuilder.Sql("""
                UPDATE LedgerAccounts
                SET Id = upper(Id)
                WHERE Code = '6400'
                  AND Id <> upper(Id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
