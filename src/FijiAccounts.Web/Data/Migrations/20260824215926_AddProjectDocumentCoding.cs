using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectDocumentCoding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProjectCostCodeId",
                table: "SupplierBillLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "SupplierBillLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectCostCodeId",
                table: "SupplierBillDrafts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "SupplierBillDrafts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectCostCodeId",
                table: "SalesInvoiceLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "SalesInvoiceLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "PurchaseOrders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DivisionId",
                table: "PurchaseOrders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectCostCodeId",
                table: "PurchaseOrderLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "PurchaseOrderLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBillLines_ProjectCostCodeId",
                table: "SupplierBillLines",
                column: "ProjectCostCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBillLines_ProjectId",
                table: "SupplierBillLines",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBillDrafts_ProjectCostCodeId",
                table: "SupplierBillDrafts",
                column: "ProjectCostCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBillDrafts_ProjectId",
                table: "SupplierBillDrafts",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_ProjectCostCodeId",
                table: "SalesInvoiceLines",
                column: "ProjectCostCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_ProjectId",
                table: "SalesInvoiceLines",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_BranchId",
                table: "PurchaseOrders",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_DivisionId",
                table: "PurchaseOrders",
                column: "DivisionId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_ProjectCostCodeId",
                table: "PurchaseOrderLines",
                column: "ProjectCostCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_ProjectId",
                table: "PurchaseOrderLines",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLines_ProjectCostCodes_ProjectCostCodeId",
                table: "PurchaseOrderLines",
                column: "ProjectCostCodeId",
                principalTable: "ProjectCostCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLines_Projects_ProjectId",
                table: "PurchaseOrderLines",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_Branches_BranchId",
                table: "PurchaseOrders",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_Divisions_DivisionId",
                table: "PurchaseOrders",
                column: "DivisionId",
                principalTable: "Divisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceLines_ProjectCostCodes_ProjectCostCodeId",
                table: "SalesInvoiceLines",
                column: "ProjectCostCodeId",
                principalTable: "ProjectCostCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceLines_Projects_ProjectId",
                table: "SalesInvoiceLines",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SupplierBillDrafts_ProjectCostCodes_ProjectCostCodeId",
                table: "SupplierBillDrafts",
                column: "ProjectCostCodeId",
                principalTable: "ProjectCostCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SupplierBillDrafts_Projects_ProjectId",
                table: "SupplierBillDrafts",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SupplierBillLines_ProjectCostCodes_ProjectCostCodeId",
                table: "SupplierBillLines",
                column: "ProjectCostCodeId",
                principalTable: "ProjectCostCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SupplierBillLines_Projects_ProjectId",
                table: "SupplierBillLines",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderLines_ProjectCostCodes_ProjectCostCodeId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderLines_Projects_ProjectId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_Branches_BranchId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_Divisions_DivisionId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceLines_ProjectCostCodes_ProjectCostCodeId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceLines_Projects_ProjectId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SupplierBillDrafts_ProjectCostCodes_ProjectCostCodeId",
                table: "SupplierBillDrafts");

            migrationBuilder.DropForeignKey(
                name: "FK_SupplierBillDrafts_Projects_ProjectId",
                table: "SupplierBillDrafts");

            migrationBuilder.DropForeignKey(
                name: "FK_SupplierBillLines_ProjectCostCodes_ProjectCostCodeId",
                table: "SupplierBillLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SupplierBillLines_Projects_ProjectId",
                table: "SupplierBillLines");

            migrationBuilder.DropIndex(
                name: "IX_SupplierBillLines_ProjectCostCodeId",
                table: "SupplierBillLines");

            migrationBuilder.DropIndex(
                name: "IX_SupplierBillLines_ProjectId",
                table: "SupplierBillLines");

            migrationBuilder.DropIndex(
                name: "IX_SupplierBillDrafts_ProjectCostCodeId",
                table: "SupplierBillDrafts");

            migrationBuilder.DropIndex(
                name: "IX_SupplierBillDrafts_ProjectId",
                table: "SupplierBillDrafts");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceLines_ProjectCostCodeId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceLines_ProjectId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_BranchId",
                table: "PurchaseOrders");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_DivisionId",
                table: "PurchaseOrders");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrderLines_ProjectCostCodeId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrderLines_ProjectId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "ProjectCostCodeId",
                table: "SupplierBillLines");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "SupplierBillLines");

            migrationBuilder.DropColumn(
                name: "ProjectCostCodeId",
                table: "SupplierBillDrafts");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "SupplierBillDrafts");

            migrationBuilder.DropColumn(
                name: "ProjectCostCodeId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "DivisionId",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "ProjectCostCodeId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "PurchaseOrderLines");
        }
    }
}
