using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AllowSupplierBillCorrectionCycles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SupplierBillVoids_SupplierBillId",
                table: "SupplierBillVoids");

            migrationBuilder.DropIndex(
                name: "IX_SupplierBillReinstatements_SupplierBillId",
                table: "SupplierBillReinstatements");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBillVoids_SupplierBillId",
                table: "SupplierBillVoids",
                column: "SupplierBillId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBillReinstatements_SupplierBillId",
                table: "SupplierBillReinstatements",
                column: "SupplierBillId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SupplierBillVoids_SupplierBillId",
                table: "SupplierBillVoids");

            migrationBuilder.DropIndex(
                name: "IX_SupplierBillReinstatements_SupplierBillId",
                table: "SupplierBillReinstatements");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBillVoids_SupplierBillId",
                table: "SupplierBillVoids",
                column: "SupplierBillId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBillReinstatements_SupplierBillId",
                table: "SupplierBillReinstatements",
                column: "SupplierBillId",
                unique: true);
        }
    }
}
