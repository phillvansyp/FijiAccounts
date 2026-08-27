using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddForeignCurrencySettlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AllocatedBaseAmount",
                table: "SupplierPayments",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "SupplierPayments",
                type: "TEXT",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRateToBase",
                table: "SupplierPayments",
                type: "TEXT",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RealisedExchangeDifference",
                table: "SupplierPayments",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionAmount",
                table: "SupplierPayments",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AllocatedBaseAmount",
                table: "SupplierPaymentApprovals",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "SupplierPaymentApprovals",
                type: "TEXT",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRateToBase",
                table: "SupplierPaymentApprovals",
                type: "TEXT",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionAmount",
                table: "SupplierPaymentApprovals",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionAmountPaid",
                table: "SupplierBills",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionAmountPaid",
                table: "SalesInvoices",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "CustomerReceipts",
                type: "TEXT",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRateToBase",
                table: "CustomerReceipts",
                type: "TEXT",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RealisedExchangeDifference",
                table: "CustomerReceipts",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionAmount",
                table: "CustomerReceipts",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionAmount",
                table: "CustomerReceiptAllocations",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE SalesInvoices
                SET TransactionAmountPaid = CASE
                    WHEN ExchangeRateToBase > 0 THEN ROUND(AmountPaid / ExchangeRateToBase, 2)
                    ELSE AmountPaid
                END;

                UPDATE SupplierBills
                SET TransactionAmountPaid = CASE
                    WHEN ExchangeRateToBase > 0 THEN ROUND(AmountPaid / ExchangeRateToBase, 2)
                    ELSE AmountPaid
                END;

                UPDATE CustomerReceiptAllocations
                SET TransactionAmount = CASE
                    WHEN (SELECT ExchangeRateToBase FROM SalesInvoices WHERE Id = CustomerReceiptAllocations.SalesInvoiceId) > 0
                    THEN ROUND(Amount / (SELECT ExchangeRateToBase FROM SalesInvoices WHERE Id = CustomerReceiptAllocations.SalesInvoiceId), 2)
                    ELSE Amount
                END;

                UPDATE CustomerReceipts
                SET Currency = COALESCE((
                        SELECT i.Currency
                        FROM CustomerReceiptAllocations a
                        JOIN SalesInvoices i ON i.Id = a.SalesInvoiceId
                        WHERE a.CustomerReceiptId = CustomerReceipts.Id
                        LIMIT 1), 'FJD'),
                    TransactionAmount = COALESCE((
                        SELECT a.TransactionAmount
                        FROM CustomerReceiptAllocations a
                        WHERE a.CustomerReceiptId = CustomerReceipts.Id
                        LIMIT 1), Amount),
                    ExchangeRateToBase = COALESCE((
                        SELECT i.ExchangeRateToBase
                        FROM CustomerReceiptAllocations a
                        JOIN SalesInvoices i ON i.Id = a.SalesInvoiceId
                        WHERE a.CustomerReceiptId = CustomerReceipts.Id
                        LIMIT 1), 1);

                UPDATE SupplierPayments
                SET Currency = COALESCE((SELECT Currency FROM SupplierBills WHERE Id = SupplierPayments.SupplierBillId), 'FJD'),
                    TransactionAmount = CASE
                        WHEN (SELECT ExchangeRateToBase FROM SupplierBills WHERE Id = SupplierPayments.SupplierBillId) > 0
                        THEN ROUND(Amount / (SELECT ExchangeRateToBase FROM SupplierBills WHERE Id = SupplierPayments.SupplierBillId), 2)
                        ELSE Amount
                    END,
                    ExchangeRateToBase = COALESCE((SELECT ExchangeRateToBase FROM SupplierBills WHERE Id = SupplierPayments.SupplierBillId), 1),
                    AllocatedBaseAmount = Amount;

                UPDATE SupplierPaymentApprovals
                SET Currency = COALESCE((SELECT Currency FROM SupplierBills WHERE Id = SupplierPaymentApprovals.SupplierBillId), 'FJD'),
                    TransactionAmount = CASE
                        WHEN (SELECT ExchangeRateToBase FROM SupplierBills WHERE Id = SupplierPaymentApprovals.SupplierBillId) > 0
                        THEN ROUND(Amount / (SELECT ExchangeRateToBase FROM SupplierBills WHERE Id = SupplierPaymentApprovals.SupplierBillId), 2)
                        ELSE Amount
                    END,
                    ExchangeRateToBase = COALESCE((SELECT ExchangeRateToBase FROM SupplierBills WHERE Id = SupplierPaymentApprovals.SupplierBillId), 1),
                    AllocatedBaseAmount = Amount;

                INSERT INTO LedgerAccounts
                    (Id, OrganisationId, Code, Name, Type, IsBankAccount, BankAccountKind, BankAccountNumber, IsSystemAccount, IsActive)
                SELECT
                    hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(6)),
                    o.Id, '4300', 'Foreign Exchange Gains', 3, 0, 0, NULL, 1, 1
                FROM Organisations o
                WHERE NOT EXISTS (SELECT 1 FROM LedgerAccounts a WHERE a.OrganisationId = o.Id AND a.Code = '4300');

                INSERT INTO LedgerAccounts
                    (Id, OrganisationId, Code, Name, Type, IsBankAccount, BankAccountKind, BankAccountNumber, IsSystemAccount, IsActive)
                SELECT
                    hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(6)),
                    o.Id, '6950', 'Foreign Exchange Losses', 4, 0, 0, NULL, 1, 1
                FROM Organisations o
                WHERE NOT EXISTS (SELECT 1 FROM LedgerAccounts a WHERE a.OrganisationId = o.Id AND a.Code = '6950');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM LedgerAccounts
                WHERE Code IN ('4300', '6950')
                  AND IsSystemAccount = 1
                  AND NOT EXISTS (
                      SELECT 1 FROM PostedJournalLines j WHERE j.LedgerAccountId = LedgerAccounts.Id
                  );
                """);

            migrationBuilder.DropColumn(
                name: "AllocatedBaseAmount",
                table: "SupplierPayments");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "SupplierPayments");

            migrationBuilder.DropColumn(
                name: "ExchangeRateToBase",
                table: "SupplierPayments");

            migrationBuilder.DropColumn(
                name: "RealisedExchangeDifference",
                table: "SupplierPayments");

            migrationBuilder.DropColumn(
                name: "TransactionAmount",
                table: "SupplierPayments");

            migrationBuilder.DropColumn(
                name: "AllocatedBaseAmount",
                table: "SupplierPaymentApprovals");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "SupplierPaymentApprovals");

            migrationBuilder.DropColumn(
                name: "ExchangeRateToBase",
                table: "SupplierPaymentApprovals");

            migrationBuilder.DropColumn(
                name: "TransactionAmount",
                table: "SupplierPaymentApprovals");

            migrationBuilder.DropColumn(
                name: "TransactionAmountPaid",
                table: "SupplierBills");

            migrationBuilder.DropColumn(
                name: "TransactionAmountPaid",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "CustomerReceipts");

            migrationBuilder.DropColumn(
                name: "ExchangeRateToBase",
                table: "CustomerReceipts");

            migrationBuilder.DropColumn(
                name: "RealisedExchangeDifference",
                table: "CustomerReceipts");

            migrationBuilder.DropColumn(
                name: "TransactionAmount",
                table: "CustomerReceipts");

            migrationBuilder.DropColumn(
                name: "TransactionAmount",
                table: "CustomerReceiptAllocations");
        }
    }
}
