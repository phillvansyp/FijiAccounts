using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFijiTaxDocumentCompliance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSimplifiedTaxInvoice",
                table: "SalesInvoices",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsTaxInvoice",
                table: "SalesInvoices",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientAddressSnapshot",
                table: "SalesInvoices",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientNameSnapshot",
                table: "SalesInvoices",
                type: "TEXT",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientTinSnapshot",
                table: "SalesInvoices",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplierAddressSnapshot",
                table: "SalesInvoices",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplierNameSnapshot",
                table: "SalesInvoices",
                type: "TEXT",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplierTinSnapshot",
                table: "SalesInvoices",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxDocumentComplianceVersion",
                table: "SalesInvoices",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AdjustedInvoiceVatAmount",
                table: "SalesCreditNotes",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalInvoiceVatAmount",
                table: "SalesCreditNotes",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "BusinessAddress",
                table: "Organisations",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsVatRegistered",
                table: "Organisations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "VatRegistrationDate",
                table: "Organisations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE Organisations
                SET BusinessAddress = COALESCE(BusinessAddress, 'Level 2, Account Island House, Suva, Fiji'),
                    IsVatRegistered = 1,
                    VatRegistrationDate = COALESCE(VatRegistrationDate, '2020-01-01')
                WHERE CountryCode = 'FJ'
                  AND OrganisationGroupId IN (
                      SELECT Id FROM OrganisationGroups WHERE IsDemo = 1
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSimplifiedTaxInvoice",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "IsTaxInvoice",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "RecipientAddressSnapshot",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "RecipientNameSnapshot",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "RecipientTinSnapshot",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "SupplierAddressSnapshot",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "SupplierNameSnapshot",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "SupplierTinSnapshot",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "TaxDocumentComplianceVersion",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "AdjustedInvoiceVatAmount",
                table: "SalesCreditNotes");

            migrationBuilder.DropColumn(
                name: "OriginalInvoiceVatAmount",
                table: "SalesCreditNotes");

            migrationBuilder.DropColumn(
                name: "BusinessAddress",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "IsVatRegistered",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "VatRegistrationDate",
                table: "Organisations");
        }
    }
}
