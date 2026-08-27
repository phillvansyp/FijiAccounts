using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContactCurrencyDefaultsAndSalesCurrencies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRateToBase",
                table: "SalesInvoices",
                type: "TEXT",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionSubtotal",
                table: "SalesInvoices",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionTotal",
                table: "SalesInvoices",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionVatTotal",
                table: "SalesInvoices",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionGrossAmount",
                table: "SalesInvoiceLines",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionNetAmount",
                table: "SalesInvoiceLines",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionUnitPrice",
                table: "SalesInvoiceLines",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionVatAmount",
                table: "SalesInvoiceLines",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "RecurringSalesInvoices",
                type: "TEXT",
                maxLength: 3,
                nullable: false,
                defaultValue: "FJD");

            migrationBuilder.AddColumn<string>(
                name: "DefaultPurchaseCurrency",
                table: "BusinessParties",
                type: "TEXT",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultSalesCurrency",
                table: "BusinessParties",
                type: "TEXT",
                maxLength: 3,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE SalesInvoices
                SET ExchangeRateToBase = 1,
                    TransactionSubtotal = Subtotal,
                    TransactionVatTotal = VatTotal,
                    TransactionTotal = Total;

                UPDATE SalesInvoiceLines
                SET TransactionUnitPrice = UnitPrice,
                    TransactionNetAmount = NetAmount,
                    TransactionVatAmount = VatAmount,
                    TransactionGrossAmount = GrossAmount;

                UPDATE RecurringSalesInvoices
                SET Currency = COALESCE(
                    (SELECT BaseCurrency FROM Organisations WHERE Organisations.Id = RecurringSalesInvoices.OrganisationId),
                    'FJD');

                UPDATE BusinessParties
                SET DefaultSalesCurrency = COALESCE(
                        (SELECT BaseCurrency FROM Organisations WHERE Organisations.Id = BusinessParties.OrganisationId),
                        'FJD'),
                    DefaultPurchaseCurrency = COALESCE(
                        (SELECT BaseCurrency FROM Organisations WHERE Organisations.Id = BusinessParties.OrganisationId),
                        'FJD');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExchangeRateToBase",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "TransactionSubtotal",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "TransactionTotal",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "TransactionVatTotal",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "TransactionGrossAmount",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "TransactionNetAmount",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "TransactionUnitPrice",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "TransactionVatAmount",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "RecurringSalesInvoices");

            migrationBuilder.DropColumn(
                name: "DefaultPurchaseCurrency",
                table: "BusinessParties");

            migrationBuilder.DropColumn(
                name: "DefaultSalesCurrency",
                table: "BusinessParties");
        }
    }
}
