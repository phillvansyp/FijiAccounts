using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierPaymentApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequireSupplierPaymentApproval",
                table: "Organisations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "SupplierPaymentApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BranchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DivisionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SupplierId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SupplierBillId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PaymentDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    BankAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StatementLineId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PurchaseApprovalPolicyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequiredApproval = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    SupplierPaymentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RequestedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DecidedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    RejectionReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierPaymentApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierPaymentApprovals_BankStatementLines_StatementLineId",
                        column: x => x.StatementLineId,
                        principalTable: "BankStatementLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPaymentApprovals_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPaymentApprovals_BusinessParties_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "BusinessParties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPaymentApprovals_Divisions_DivisionId",
                        column: x => x.DivisionId,
                        principalTable: "Divisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPaymentApprovals_LedgerAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPaymentApprovals_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPaymentApprovals_PurchaseApprovalPolicies_PurchaseApprovalPolicyId",
                        column: x => x.PurchaseApprovalPolicyId,
                        principalTable: "PurchaseApprovalPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SupplierPaymentApprovals_SupplierBills_SupplierBillId",
                        column: x => x.SupplierBillId,
                        principalTable: "SupplierBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPaymentApprovals_SupplierPayments_SupplierPaymentId",
                        column: x => x.SupplierPaymentId,
                        principalTable: "SupplierPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaymentApprovals_BankAccountId",
                table: "SupplierPaymentApprovals",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaymentApprovals_BranchId",
                table: "SupplierPaymentApprovals",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaymentApprovals_DivisionId",
                table: "SupplierPaymentApprovals",
                column: "DivisionId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaymentApprovals_OrganisationId_Status_RequestedAt",
                table: "SupplierPaymentApprovals",
                columns: new[] { "OrganisationId", "Status", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaymentApprovals_PurchaseApprovalPolicyId",
                table: "SupplierPaymentApprovals",
                column: "PurchaseApprovalPolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaymentApprovals_StatementLineId",
                table: "SupplierPaymentApprovals",
                column: "StatementLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaymentApprovals_SupplierBillId",
                table: "SupplierPaymentApprovals",
                column: "SupplierBillId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaymentApprovals_SupplierId",
                table: "SupplierPaymentApprovals",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaymentApprovals_SupplierPaymentId",
                table: "SupplierPaymentApprovals",
                column: "SupplierPaymentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupplierPaymentApprovals");

            migrationBuilder.DropColumn(
                name: "RequireSupplierPaymentApproval",
                table: "Organisations");
        }
    }
}
