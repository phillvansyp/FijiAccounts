using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeLegacyLedgerAccountIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQLite stores Guid values as TEXT and compares foreign keys using the
            // stored casing. Early seed migrations used lower-case Guid strings,
            // while EF writes Guid parameters in upper case. Normalise the complete
            // ledger-account key graph outside the migration transaction.
            migrationBuilder.Sql(
                """
                PRAGMA foreign_keys = OFF;
                UPDATE PostedJournalLines SET LedgerAccountId = UPPER(LedgerAccountId);
                UPDATE BankStatementLines SET BankAccountId = UPPER(BankAccountId);
                UPDATE BankTransfers SET FromBankAccountId = UPPER(FromBankAccountId), ToBankAccountId = UPPER(ToBankAccountId);
                UPDATE BankRules SET TargetAccountId = UPPER(TargetAccountId);
                UPDATE AccountBudgets SET LedgerAccountId = UPPER(LedgerAccountId);
                UPDATE SalesInvoiceLines SET RevenueAccountId = UPPER(RevenueAccountId);
                UPDATE SalesQuoteLines SET RevenueAccountId = UPPER(RevenueAccountId);
                UPDATE CustomerReceipts SET BankAccountId = UPPER(BankAccountId);
                UPDATE SupplierBillLines SET ExpenseAccountId = UPPER(ExpenseAccountId);
                UPDATE SupplierPayments SET BankAccountId = UPPER(BankAccountId);
                UPDATE FixedAssets SET AssetAccountId = UPPER(AssetAccountId), DepreciationExpenseAccountId = UPPER(DepreciationExpenseAccountId), AccumulatedDepreciationAccountId = UPPER(AccumulatedDepreciationAccountId);
                UPDATE ProductItems SET RevenueAccountId = UPPER(RevenueAccountId), ExpenseAccountId = UPPER(ExpenseAccountId), InventoryAccountId = UPPER(InventoryAccountId), CostAdjustmentAccountId = UPPER(CostAdjustmentAccountId);
                UPDATE LedgerAccounts SET Id = UPPER(Id);
                PRAGMA foreign_keys = ON;
                """,
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Casing is not semantically reversible and upper-case is EF's canonical
            // SQLite representation for Guid values.
        }
    }
}
