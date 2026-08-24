using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AllowCorrectingVoidedSupplierBill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SupplierBills_OrganisationId_SupplierId_SupplierReference",
                table: "SupplierBills");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBills_OrganisationId_SupplierId_SupplierReference",
                table: "SupplierBills",
                columns: new[] { "OrganisationId", "SupplierId", "SupplierReference" },
                unique: true,
                filter: "\"Status\" <> 4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SupplierBills_OrganisationId_SupplierId_SupplierReference",
                table: "SupplierBills");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBills_OrganisationId_SupplierId_SupplierReference",
                table: "SupplierBills",
                columns: new[] { "OrganisationId", "SupplierId", "SupplierReference" },
                unique: true);
        }
    }
}
