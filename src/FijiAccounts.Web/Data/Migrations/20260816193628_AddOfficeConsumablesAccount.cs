using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOfficeConsumablesAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO LedgerAccounts
                    (Id, OrganisationId, Code, Name, Type, IsBankAccount, BankAccountNumber, IsSystemAccount, IsActive)
                SELECT
                    hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(6)),
                    o.Id,
                    '6500',
                    'Office Consumables',
                    4,
                    0,
                    NULL,
                    1,
                    1
                FROM Organisations o
                WHERE o.CountryCode = 'FJ'
                  AND NOT EXISTS (
                      SELECT 1 FROM LedgerAccounts a
                      WHERE a.OrganisationId = o.Id AND a.Code = '6500'
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM LedgerAccounts
                WHERE Code = '6500'
                  AND Name = 'Office Consumables'
                  AND IsSystemAccount = 1
                  AND NOT EXISTS (
                      SELECT 1 FROM PostedJournalLines j WHERE j.LedgerAccountId = LedgerAccounts.Id
                  );
                """);
        }
    }
}
