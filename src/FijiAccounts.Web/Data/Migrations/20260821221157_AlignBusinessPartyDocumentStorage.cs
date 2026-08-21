using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AlignBusinessPartyDocumentStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "StoredSize",
                table: "BusinessPartyDocuments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StoredSize",
                table: "BusinessPartyDocuments");
        }
    }
}
