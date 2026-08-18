using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesQuotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SalesQuotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SequenceNumber = table.Column<long>(type: "INTEGER", nullable: false),
                    QuoteNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    QuoteDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Subtotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    VatTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ConvertedInvoiceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesQuotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesQuotes_BusinessParties_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "BusinessParties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesQuotes_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesQuotes_SalesInvoices_ConvertedInvoiceId",
                        column: x => x.ConvertedInvoiceId,
                        principalTable: "SalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesQuoteLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SalesQuoteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    VatTreatment = table.Column<int>(type: "INTEGER", nullable: false),
                    VatRate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    NetAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    VatAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    GrossAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    RevenueAccountId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesQuoteLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesQuoteLines_LedgerAccounts_RevenueAccountId",
                        column: x => x.RevenueAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesQuoteLines_SalesQuotes_SalesQuoteId",
                        column: x => x.SalesQuoteId,
                        principalTable: "SalesQuotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuoteLines_RevenueAccountId",
                table: "SalesQuoteLines",
                column: "RevenueAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuoteLines_SalesQuoteId",
                table: "SalesQuoteLines",
                column: "SalesQuoteId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuotes_ConvertedInvoiceId",
                table: "SalesQuotes",
                column: "ConvertedInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuotes_CustomerId",
                table: "SalesQuotes",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuotes_OrganisationId_QuoteNumber",
                table: "SalesQuotes",
                columns: new[] { "OrganisationId", "QuoteNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuotes_OrganisationId_SequenceNumber",
                table: "SalesQuotes",
                columns: new[] { "OrganisationId", "SequenceNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesQuoteLines");

            migrationBuilder.DropTable(
                name: "SalesQuotes");
        }
    }
}
