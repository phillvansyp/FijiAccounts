using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollServiceBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PayrollServiceBillingImport",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceBillId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceCustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    SalesInvoiceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayloadHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceJson = table.Column<string>(type: "TEXT", nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollServiceBillingImport", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollServiceBillingImport_SalesInvoices_SalesInvoiceId",
                        column: x => x.SalesInvoiceId,
                        principalTable: "SalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollServiceBillingImport_OrganisationId_SourceCustomerId_PeriodStart",
                table: "PayrollServiceBillingImport",
                columns: new[] { "OrganisationId", "SourceCustomerId", "PeriodStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollServiceBillingImport_SalesInvoiceId",
                table: "PayrollServiceBillingImport",
                column: "SalesInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollServiceBillingImport_SourceBillId",
                table: "PayrollServiceBillingImport",
                column: "SourceBillId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollServiceBillingImport");
        }
    }
}
