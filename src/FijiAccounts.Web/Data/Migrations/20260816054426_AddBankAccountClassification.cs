using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddBankAccountClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BankAccountNumber",
                table: "LedgerAccounts",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBankAccount",
                table: "LedgerAccounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("UPDATE LedgerAccounts SET IsBankAccount = 1 WHERE Code = '1000'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BankAccountNumber",
                table: "LedgerAccounts");

            migrationBuilder.DropColumn(
                name: "IsBankAccount",
                table: "LedgerAccounts");
        }
    }
}
