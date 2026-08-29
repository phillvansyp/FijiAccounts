using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFiscalisationWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FiscalisationRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SalesInvoiceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RequestJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    SdcInvoiceNumber = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    SdcIssuedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    VerificationUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    VerificationQrCode = table.Column<string>(type: "TEXT", nullable: true),
                    SignedPayload = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalisationRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FiscalisationRecords_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FiscalisationRecords_SalesInvoices_SalesInvoiceId",
                        column: x => x.SalesInvoiceId,
                        principalTable: "SalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalisationRecords_OrganisationId_Status",
                table: "FiscalisationRecords",
                columns: new[] { "OrganisationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalisationRecords_SalesInvoiceId",
                table: "FiscalisationRecords",
                column: "SalesInvoiceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FiscalisationRecords");
        }
    }
}
