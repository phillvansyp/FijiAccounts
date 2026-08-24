using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectWorkflowCoding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProjectCostCodeId",
                table: "RecurringSupplierBillLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "RecurringSupplierBillLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectCostCodeId",
                table: "RecurringSalesInvoiceLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "RecurringSalesInvoiceLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectCostCodeId",
                table: "PurchaseRequisitionLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "PurchaseRequisitionLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSupplierBillLines_ProjectCostCodeId",
                table: "RecurringSupplierBillLines",
                column: "ProjectCostCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSupplierBillLines_ProjectId",
                table: "RecurringSupplierBillLines",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSalesInvoiceLines_ProjectCostCodeId",
                table: "RecurringSalesInvoiceLines",
                column: "ProjectCostCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSalesInvoiceLines_ProjectId",
                table: "RecurringSalesInvoiceLines",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitionLines_ProjectCostCodeId",
                table: "PurchaseRequisitionLines",
                column: "ProjectCostCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitionLines_ProjectId",
                table: "PurchaseRequisitionLines",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseRequisitionLines_ProjectCostCodes_ProjectCostCodeId",
                table: "PurchaseRequisitionLines",
                column: "ProjectCostCodeId",
                principalTable: "ProjectCostCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseRequisitionLines_Projects_ProjectId",
                table: "PurchaseRequisitionLines",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringSalesInvoiceLines_ProjectCostCodes_ProjectCostCodeId",
                table: "RecurringSalesInvoiceLines",
                column: "ProjectCostCodeId",
                principalTable: "ProjectCostCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringSalesInvoiceLines_Projects_ProjectId",
                table: "RecurringSalesInvoiceLines",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringSupplierBillLines_ProjectCostCodes_ProjectCostCodeId",
                table: "RecurringSupplierBillLines",
                column: "ProjectCostCodeId",
                principalTable: "ProjectCostCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringSupplierBillLines_Projects_ProjectId",
                table: "RecurringSupplierBillLines",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseRequisitionLines_ProjectCostCodes_ProjectCostCodeId",
                table: "PurchaseRequisitionLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseRequisitionLines_Projects_ProjectId",
                table: "PurchaseRequisitionLines");

            migrationBuilder.DropForeignKey(
                name: "FK_RecurringSalesInvoiceLines_ProjectCostCodes_ProjectCostCodeId",
                table: "RecurringSalesInvoiceLines");

            migrationBuilder.DropForeignKey(
                name: "FK_RecurringSalesInvoiceLines_Projects_ProjectId",
                table: "RecurringSalesInvoiceLines");

            migrationBuilder.DropForeignKey(
                name: "FK_RecurringSupplierBillLines_ProjectCostCodes_ProjectCostCodeId",
                table: "RecurringSupplierBillLines");

            migrationBuilder.DropForeignKey(
                name: "FK_RecurringSupplierBillLines_Projects_ProjectId",
                table: "RecurringSupplierBillLines");

            migrationBuilder.DropIndex(
                name: "IX_RecurringSupplierBillLines_ProjectCostCodeId",
                table: "RecurringSupplierBillLines");

            migrationBuilder.DropIndex(
                name: "IX_RecurringSupplierBillLines_ProjectId",
                table: "RecurringSupplierBillLines");

            migrationBuilder.DropIndex(
                name: "IX_RecurringSalesInvoiceLines_ProjectCostCodeId",
                table: "RecurringSalesInvoiceLines");

            migrationBuilder.DropIndex(
                name: "IX_RecurringSalesInvoiceLines_ProjectId",
                table: "RecurringSalesInvoiceLines");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseRequisitionLines_ProjectCostCodeId",
                table: "PurchaseRequisitionLines");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseRequisitionLines_ProjectId",
                table: "PurchaseRequisitionLines");

            migrationBuilder.DropColumn(
                name: "ProjectCostCodeId",
                table: "RecurringSupplierBillLines");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "RecurringSupplierBillLines");

            migrationBuilder.DropColumn(
                name: "ProjectCostCodeId",
                table: "RecurringSalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "RecurringSalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "ProjectCostCodeId",
                table: "PurchaseRequisitionLines");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "PurchaseRequisitionLines");
        }
    }
}
