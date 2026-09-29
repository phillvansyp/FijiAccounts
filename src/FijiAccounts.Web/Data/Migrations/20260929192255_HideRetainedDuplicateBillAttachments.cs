using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class HideRetainedDuplicateBillAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HiddenAsDuplicate",
                table: "SupplierBillAttachments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HiddenAsDuplicateAt",
                table: "SupplierBillAttachments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HiddenAsDuplicateByUserId",
                table: "SupplierBillAttachments",
                type: "TEXT",
                maxLength: 450,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HiddenAsDuplicate",
                table: "SupplierBillAttachments");

            migrationBuilder.DropColumn(
                name: "HiddenAsDuplicateAt",
                table: "SupplierBillAttachments");

            migrationBuilder.DropColumn(
                name: "HiddenAsDuplicateByUserId",
                table: "SupplierBillAttachments");
        }
    }
}
