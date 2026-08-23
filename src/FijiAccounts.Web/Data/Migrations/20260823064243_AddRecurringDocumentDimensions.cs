using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringDocumentDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "RecurringSupplierBills",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DivisionId",
                table: "RecurringSupplierBills",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "RecurringSalesInvoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DivisionId",
                table: "RecurringSalesInvoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "RecurringSalesInvoices"
                SET "BranchId" = (
                    SELECT b."Id" FROM "Branches" b
                    WHERE b."OrganisationId" = "RecurringSalesInvoices"."OrganisationId"
                        AND b."IsDefault" = 1 LIMIT 1);
                UPDATE "RecurringSalesInvoices"
                SET "DivisionId" = (
                    SELECT d."Id" FROM "Divisions" d
                    WHERE d."BranchId" = "RecurringSalesInvoices"."BranchId"
                        AND d."IsDefault" = 1 LIMIT 1);

                UPDATE "RecurringSupplierBills"
                SET "BranchId" = (
                    SELECT b."Id" FROM "Branches" b
                    WHERE b."OrganisationId" = "RecurringSupplierBills"."OrganisationId"
                        AND b."IsDefault" = 1 LIMIT 1);
                UPDATE "RecurringSupplierBills"
                SET "DivisionId" = (
                    SELECT d."Id" FROM "Divisions" d
                    WHERE d."BranchId" = "RecurringSupplierBills"."BranchId"
                        AND d."IsDefault" = 1 LIMIT 1);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSupplierBills_BranchId_DivisionId",
                table: "RecurringSupplierBills",
                columns: new[] { "BranchId", "DivisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSupplierBills_DivisionId",
                table: "RecurringSupplierBills",
                column: "DivisionId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSalesInvoices_BranchId_DivisionId",
                table: "RecurringSalesInvoices",
                columns: new[] { "BranchId", "DivisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSalesInvoices_DivisionId",
                table: "RecurringSalesInvoices",
                column: "DivisionId");

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringSalesInvoices_Branches_BranchId",
                table: "RecurringSalesInvoices",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringSalesInvoices_Divisions_DivisionId",
                table: "RecurringSalesInvoices",
                column: "DivisionId",
                principalTable: "Divisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringSupplierBills_Branches_BranchId",
                table: "RecurringSupplierBills",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringSupplierBills_Divisions_DivisionId",
                table: "RecurringSupplierBills",
                column: "DivisionId",
                principalTable: "Divisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RecurringSalesInvoices_Branches_BranchId",
                table: "RecurringSalesInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_RecurringSalesInvoices_Divisions_DivisionId",
                table: "RecurringSalesInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_RecurringSupplierBills_Branches_BranchId",
                table: "RecurringSupplierBills");

            migrationBuilder.DropForeignKey(
                name: "FK_RecurringSupplierBills_Divisions_DivisionId",
                table: "RecurringSupplierBills");

            migrationBuilder.DropIndex(
                name: "IX_RecurringSupplierBills_BranchId_DivisionId",
                table: "RecurringSupplierBills");

            migrationBuilder.DropIndex(
                name: "IX_RecurringSupplierBills_DivisionId",
                table: "RecurringSupplierBills");

            migrationBuilder.DropIndex(
                name: "IX_RecurringSalesInvoices_BranchId_DivisionId",
                table: "RecurringSalesInvoices");

            migrationBuilder.DropIndex(
                name: "IX_RecurringSalesInvoices_DivisionId",
                table: "RecurringSalesInvoices");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "RecurringSupplierBills");

            migrationBuilder.DropColumn(
                name: "DivisionId",
                table: "RecurringSupplierBills");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "RecurringSalesInvoices");

            migrationBuilder.DropColumn(
                name: "DivisionId",
                table: "RecurringSalesInvoices");
        }
    }
}
