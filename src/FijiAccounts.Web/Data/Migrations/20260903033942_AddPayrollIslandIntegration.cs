using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollIslandIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PayrollIslandConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    PayrollOrganisationId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ProtectedAccessToken = table.Column<string>(type: "TEXT", nullable: false),
                    WagesExpenseAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployerContributionsExpenseAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NetWagesPayableAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayePayableAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FnpfPayableAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OtherDeductionsPayableAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastSyncCursor = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastSyncError = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollIslandConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollIslandConnections_LedgerAccounts_EmployerContributionsExpenseAccountId",
                        column: x => x.EmployerContributionsExpenseAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollIslandConnections_LedgerAccounts_FnpfPayableAccountId",
                        column: x => x.FnpfPayableAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollIslandConnections_LedgerAccounts_NetWagesPayableAccountId",
                        column: x => x.NetWagesPayableAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollIslandConnections_LedgerAccounts_OtherDeductionsPayableAccountId",
                        column: x => x.OtherDeductionsPayableAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollIslandConnections_LedgerAccounts_PayePayableAccountId",
                        column: x => x.PayePayableAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollIslandConnections_LedgerAccounts_WagesExpenseAccountId",
                        column: x => x.WagesExpenseAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollIslandConnections_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayrollIslandPayRunImports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExternalPayRunId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    PayRunNumber = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PaymentDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    EmployeeCount = table.Column<int>(type: "INTEGER", nullable: false),
                    GrossEarnings = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    EmployeePaye = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    EmployeeFnpf = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    EmployerFnpf = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    OtherDeductions = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    NetPay = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    PayloadSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    PostedJournalId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ImportedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ImportedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollIslandPayRunImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollIslandPayRunImports_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollIslandPayRunImports_PayrollIslandConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "PayrollIslandConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollIslandPayRunImports_PostedJournals_PostedJournalId",
                        column: x => x.PostedJournalId,
                        principalTable: "PostedJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollIslandPaymentRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayRunImportId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExternalPaymentId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PaidDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollIslandPaymentRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollIslandPaymentRecords_PayrollIslandPayRunImports_PayRunImportId",
                        column: x => x.PayRunImportId,
                        principalTable: "PayrollIslandPayRunImports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollIslandConnections_EmployerContributionsExpenseAccountId",
                table: "PayrollIslandConnections",
                column: "EmployerContributionsExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollIslandConnections_FnpfPayableAccountId",
                table: "PayrollIslandConnections",
                column: "FnpfPayableAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollIslandConnections_NetWagesPayableAccountId",
                table: "PayrollIslandConnections",
                column: "NetWagesPayableAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollIslandConnections_OrganisationId",
                table: "PayrollIslandConnections",
                column: "OrganisationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollIslandConnections_OtherDeductionsPayableAccountId",
                table: "PayrollIslandConnections",
                column: "OtherDeductionsPayableAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollIslandConnections_PayePayableAccountId",
                table: "PayrollIslandConnections",
                column: "PayePayableAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollIslandConnections_WagesExpenseAccountId",
                table: "PayrollIslandConnections",
                column: "WagesExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollIslandPaymentRecords_PayRunImportId_ExternalPaymentId",
                table: "PayrollIslandPaymentRecords",
                columns: new[] { "PayRunImportId", "ExternalPaymentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollIslandPayRunImports_ConnectionId_ExternalPayRunId_Revision",
                table: "PayrollIslandPayRunImports",
                columns: new[] { "ConnectionId", "ExternalPayRunId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollIslandPayRunImports_OrganisationId_Status_PaymentDate",
                table: "PayrollIslandPayRunImports",
                columns: new[] { "OrganisationId", "Status", "PaymentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollIslandPayRunImports_PostedJournalId",
                table: "PayrollIslandPayRunImports",
                column: "PostedJournalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollIslandPaymentRecords");

            migrationBuilder.DropTable(
                name: "PayrollIslandPayRunImports");

            migrationBuilder.DropTable(
                name: "PayrollIslandConnections");
        }
    }
}
