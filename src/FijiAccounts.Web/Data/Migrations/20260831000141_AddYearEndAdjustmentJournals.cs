using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddYearEndAdjustmentJournals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AdjustmentPeriodId",
                table: "PostedJournals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovalReference",
                table: "PostedJournals",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Purpose",
                table: "PostedJournals",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_PostedJournals_AdjustmentPeriodId",
                table: "PostedJournals",
                column: "AdjustmentPeriodId");

            migrationBuilder.AddForeignKey(
                name: "FK_PostedJournals_AccountingPeriods_AdjustmentPeriodId",
                table: "PostedJournals",
                column: "AdjustmentPeriodId",
                principalTable: "AccountingPeriods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PostedJournals_AccountingPeriods_AdjustmentPeriodId",
                table: "PostedJournals");

            migrationBuilder.DropIndex(
                name: "IX_PostedJournals_AdjustmentPeriodId",
                table: "PostedJournals");

            migrationBuilder.DropColumn(
                name: "AdjustmentPeriodId",
                table: "PostedJournals");

            migrationBuilder.DropColumn(
                name: "ApprovalReference",
                table: "PostedJournals");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "PostedJournals");
        }
    }
}
