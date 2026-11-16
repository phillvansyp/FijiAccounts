using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddXeroSyncCheckpoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SyncCheckpointJson",
                table: "XeroConnections",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SyncCheckpointJson",
                table: "XeroConnections");
        }
    }
}
