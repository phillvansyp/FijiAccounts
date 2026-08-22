using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialDocumentDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "SupplierPayments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DivisionId",
                table: "SupplierPayments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "SupplierBills",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DivisionId",
                table: "SupplierBills",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "SupplierBillDrafts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DivisionId",
                table: "SupplierBillDrafts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "SalesInvoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DivisionId",
                table: "SalesInvoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "CustomerReceipts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DivisionId",
                table: "CustomerReceipts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "SalesInvoices"
                SET "BranchId" = (
                    SELECT b."Id" FROM "Branches" b
                    WHERE b."OrganisationId" = "SalesInvoices"."OrganisationId"
                        AND b."IsDefault" = 1 LIMIT 1);
                UPDATE "SalesInvoices"
                SET "DivisionId" = (
                    SELECT d."Id" FROM "Divisions" d
                    WHERE d."BranchId" = "SalesInvoices"."BranchId"
                        AND d."IsDefault" = 1 LIMIT 1);

                UPDATE "SupplierBills"
                SET "BranchId" = (
                    SELECT b."Id" FROM "Branches" b
                    WHERE b."OrganisationId" = "SupplierBills"."OrganisationId"
                        AND b."IsDefault" = 1 LIMIT 1);
                UPDATE "SupplierBills"
                SET "DivisionId" = (
                    SELECT d."Id" FROM "Divisions" d
                    WHERE d."BranchId" = "SupplierBills"."BranchId"
                        AND d."IsDefault" = 1 LIMIT 1);

                UPDATE "SupplierBillDrafts"
                SET "BranchId" = (
                    SELECT b."Id" FROM "Branches" b
                    WHERE b."OrganisationId" = "SupplierBillDrafts"."OrganisationId"
                        AND b."IsDefault" = 1 LIMIT 1);
                UPDATE "SupplierBillDrafts"
                SET "DivisionId" = (
                    SELECT d."Id" FROM "Divisions" d
                    WHERE d."BranchId" = "SupplierBillDrafts"."BranchId"
                        AND d."IsDefault" = 1 LIMIT 1);

                UPDATE "CustomerReceipts"
                SET "BranchId" = (
                        SELECT i."BranchId"
                        FROM "CustomerReceiptAllocations" a
                        INNER JOIN "SalesInvoices" i ON i."Id" = a."SalesInvoiceId"
                        WHERE a."CustomerReceiptId" = "CustomerReceipts"."Id"
                        LIMIT 1),
                    "DivisionId" = (
                        SELECT i."DivisionId"
                        FROM "CustomerReceiptAllocations" a
                        INNER JOIN "SalesInvoices" i ON i."Id" = a."SalesInvoiceId"
                        WHERE a."CustomerReceiptId" = "CustomerReceipts"."Id"
                        LIMIT 1);

                UPDATE "SupplierPayments"
                SET "BranchId" = (
                        SELECT b."BranchId" FROM "SupplierBills" b
                        WHERE b."Id" = "SupplierPayments"."SupplierBillId"),
                    "DivisionId" = (
                        SELECT b."DivisionId" FROM "SupplierBills" b
                        WHERE b."Id" = "SupplierPayments"."SupplierBillId");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_BranchId_DivisionId",
                table: "SupplierPayments",
                columns: new[] { "BranchId", "DivisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_DivisionId",
                table: "SupplierPayments",
                column: "DivisionId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBills_BranchId_DivisionId",
                table: "SupplierBills",
                columns: new[] { "BranchId", "DivisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBills_DivisionId",
                table: "SupplierBills",
                column: "DivisionId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBillDrafts_BranchId_DivisionId",
                table: "SupplierBillDrafts",
                columns: new[] { "BranchId", "DivisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBillDrafts_DivisionId",
                table: "SupplierBillDrafts",
                column: "DivisionId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_BranchId_DivisionId",
                table: "SalesInvoices",
                columns: new[] { "BranchId", "DivisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_DivisionId",
                table: "SalesInvoices",
                column: "DivisionId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceipts_BranchId_DivisionId",
                table: "CustomerReceipts",
                columns: new[] { "BranchId", "DivisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceipts_DivisionId",
                table: "CustomerReceipts",
                column: "DivisionId");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerReceipts_Branches_BranchId",
                table: "CustomerReceipts",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerReceipts_Divisions_DivisionId",
                table: "CustomerReceipts",
                column: "DivisionId",
                principalTable: "Divisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoices_Branches_BranchId",
                table: "SalesInvoices",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoices_Divisions_DivisionId",
                table: "SalesInvoices",
                column: "DivisionId",
                principalTable: "Divisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SupplierBillDrafts_Branches_BranchId",
                table: "SupplierBillDrafts",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SupplierBillDrafts_Divisions_DivisionId",
                table: "SupplierBillDrafts",
                column: "DivisionId",
                principalTable: "Divisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SupplierBills_Branches_BranchId",
                table: "SupplierBills",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SupplierBills_Divisions_DivisionId",
                table: "SupplierBills",
                column: "DivisionId",
                principalTable: "Divisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SupplierPayments_Branches_BranchId",
                table: "SupplierPayments",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SupplierPayments_Divisions_DivisionId",
                table: "SupplierPayments",
                column: "DivisionId",
                principalTable: "Divisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerReceipts_Branches_BranchId",
                table: "CustomerReceipts");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerReceipts_Divisions_DivisionId",
                table: "CustomerReceipts");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoices_Branches_BranchId",
                table: "SalesInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoices_Divisions_DivisionId",
                table: "SalesInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_SupplierBillDrafts_Branches_BranchId",
                table: "SupplierBillDrafts");

            migrationBuilder.DropForeignKey(
                name: "FK_SupplierBillDrafts_Divisions_DivisionId",
                table: "SupplierBillDrafts");

            migrationBuilder.DropForeignKey(
                name: "FK_SupplierBills_Branches_BranchId",
                table: "SupplierBills");

            migrationBuilder.DropForeignKey(
                name: "FK_SupplierBills_Divisions_DivisionId",
                table: "SupplierBills");

            migrationBuilder.DropForeignKey(
                name: "FK_SupplierPayments_Branches_BranchId",
                table: "SupplierPayments");

            migrationBuilder.DropForeignKey(
                name: "FK_SupplierPayments_Divisions_DivisionId",
                table: "SupplierPayments");

            migrationBuilder.DropIndex(
                name: "IX_SupplierPayments_BranchId_DivisionId",
                table: "SupplierPayments");

            migrationBuilder.DropIndex(
                name: "IX_SupplierPayments_DivisionId",
                table: "SupplierPayments");

            migrationBuilder.DropIndex(
                name: "IX_SupplierBills_BranchId_DivisionId",
                table: "SupplierBills");

            migrationBuilder.DropIndex(
                name: "IX_SupplierBills_DivisionId",
                table: "SupplierBills");

            migrationBuilder.DropIndex(
                name: "IX_SupplierBillDrafts_BranchId_DivisionId",
                table: "SupplierBillDrafts");

            migrationBuilder.DropIndex(
                name: "IX_SupplierBillDrafts_DivisionId",
                table: "SupplierBillDrafts");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoices_BranchId_DivisionId",
                table: "SalesInvoices");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoices_DivisionId",
                table: "SalesInvoices");

            migrationBuilder.DropIndex(
                name: "IX_CustomerReceipts_BranchId_DivisionId",
                table: "CustomerReceipts");

            migrationBuilder.DropIndex(
                name: "IX_CustomerReceipts_DivisionId",
                table: "CustomerReceipts");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "SupplierPayments");

            migrationBuilder.DropColumn(
                name: "DivisionId",
                table: "SupplierPayments");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "SupplierBills");

            migrationBuilder.DropColumn(
                name: "DivisionId",
                table: "SupplierBills");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "SupplierBillDrafts");

            migrationBuilder.DropColumn(
                name: "DivisionId",
                table: "SupplierBillDrafts");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "DivisionId",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "CustomerReceipts");

            migrationBuilder.DropColumn(
                name: "DivisionId",
                table: "CustomerReceipts");
        }
    }
}
