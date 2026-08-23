using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseApprovalPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseApprovalPolicyId",
                table: "PurchaseRequisitions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequiredApproval",
                table: "PurchaseRequisitions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PurchaseApprovalPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BranchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DivisionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    MinimumAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    MaximumAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    Requirement = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseApprovalPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseApprovalPolicies_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseApprovalPolicies_Divisions_DivisionId",
                        column: x => x.DivisionId,
                        principalTable: "Divisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseApprovalPolicies_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitions_PurchaseApprovalPolicyId",
                table: "PurchaseRequisitions",
                column: "PurchaseApprovalPolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseApprovalPolicies_BranchId",
                table: "PurchaseApprovalPolicies",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseApprovalPolicies_DivisionId",
                table: "PurchaseApprovalPolicies",
                column: "DivisionId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseApprovalPolicies_OrganisationId_IsActive_MinimumAmount",
                table: "PurchaseApprovalPolicies",
                columns: new[] { "OrganisationId", "IsActive", "MinimumAmount" });

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseRequisitions_PurchaseApprovalPolicies_PurchaseApprovalPolicyId",
                table: "PurchaseRequisitions",
                column: "PurchaseApprovalPolicyId",
                principalTable: "PurchaseApprovalPolicies",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseRequisitions_PurchaseApprovalPolicies_PurchaseApprovalPolicyId",
                table: "PurchaseRequisitions");

            migrationBuilder.DropTable(
                name: "PurchaseApprovalPolicies");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseRequisitions_PurchaseApprovalPolicyId",
                table: "PurchaseRequisitions");

            migrationBuilder.DropColumn(
                name: "PurchaseApprovalPolicyId",
                table: "PurchaseRequisitions");

            migrationBuilder.DropColumn(
                name: "RequiredApproval",
                table: "PurchaseRequisitions");
        }
    }
}
