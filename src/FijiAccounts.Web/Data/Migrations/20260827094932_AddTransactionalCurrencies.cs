using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionalCurrencies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRateToBase",
                table: "SupplierBills",
                type: "TEXT",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionSubtotal",
                table: "SupplierBills",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionTotal",
                table: "SupplierBills",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionVatTotal",
                table: "SupplierBills",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionGrossAmount",
                table: "SupplierBillLines",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionNetAmount",
                table: "SupplierBillLines",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionUnitPrice",
                table: "SupplierBillLines",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionVatAmount",
                table: "SupplierBillLines",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "SupplierBillDrafts",
                type: "TEXT",
                maxLength: 3,
                nullable: false,
                defaultValue: "FJD");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRateToBase",
                table: "SupplierBillDrafts",
                type: "TEXT",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "RecurringSupplierBills",
                type: "TEXT",
                maxLength: 3,
                nullable: false,
                defaultValue: "FJD");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRateToBase",
                table: "RecurringSupplierBills",
                type: "TEXT",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.CreateTable(
                name: "OrganisationCurrencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganisationCurrencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganisationCurrencies_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransactionExchangeRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    ToCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Rate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 8, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionExchangeRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransactionExchangeRates_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganisationCurrencies_OrganisationId_Code",
                table: "OrganisationCurrencies",
                columns: new[] { "OrganisationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransactionExchangeRates_OrganisationId_FromCurrency_ToCurrency_EffectiveDate",
                table: "TransactionExchangeRates",
                columns: new[] { "OrganisationId", "FromCurrency", "ToCurrency", "EffectiveDate" },
                unique: true);

            migrationBuilder.Sql("""
                UPDATE SupplierBills
                SET ExchangeRateToBase = 1,
                    TransactionSubtotal = Subtotal,
                    TransactionVatTotal = VatTotal,
                    TransactionTotal = Total;

                UPDATE SupplierBillLines
                SET TransactionUnitPrice = UnitPrice,
                    TransactionNetAmount = NetAmount,
                    TransactionVatAmount = VatAmount,
                    TransactionGrossAmount = GrossAmount;

                UPDATE SupplierBillDrafts
                SET Currency = COALESCE(
                        (SELECT BaseCurrency FROM Organisations WHERE Organisations.Id = SupplierBillDrafts.OrganisationId),
                        'FJD'),
                    ExchangeRateToBase = 1;

                UPDATE RecurringSupplierBills
                SET Currency = COALESCE(
                        (SELECT BaseCurrency FROM Organisations WHERE Organisations.Id = RecurringSupplierBills.OrganisationId),
                        'FJD'),
                    ExchangeRateToBase = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganisationCurrencies");

            migrationBuilder.DropTable(
                name: "TransactionExchangeRates");

            migrationBuilder.DropColumn(
                name: "ExchangeRateToBase",
                table: "SupplierBills");

            migrationBuilder.DropColumn(
                name: "TransactionSubtotal",
                table: "SupplierBills");

            migrationBuilder.DropColumn(
                name: "TransactionTotal",
                table: "SupplierBills");

            migrationBuilder.DropColumn(
                name: "TransactionVatTotal",
                table: "SupplierBills");

            migrationBuilder.DropColumn(
                name: "TransactionGrossAmount",
                table: "SupplierBillLines");

            migrationBuilder.DropColumn(
                name: "TransactionNetAmount",
                table: "SupplierBillLines");

            migrationBuilder.DropColumn(
                name: "TransactionUnitPrice",
                table: "SupplierBillLines");

            migrationBuilder.DropColumn(
                name: "TransactionVatAmount",
                table: "SupplierBillLines");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "SupplierBillDrafts");

            migrationBuilder.DropColumn(
                name: "ExchangeRateToBase",
                table: "SupplierBillDrafts");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "RecurringSupplierBills");

            migrationBuilder.DropColumn(
                name: "ExchangeRateToBase",
                table: "RecurringSupplierBills");
        }
    }
}
