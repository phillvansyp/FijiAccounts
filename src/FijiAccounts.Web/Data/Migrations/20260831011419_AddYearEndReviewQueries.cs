using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddYearEndReviewQueries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QueryAssignedToUserId",
                table: "YearEndReviewItems",
                type: "TEXT",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "QueryDueDate",
                table: "YearEndReviewItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "QueryRaisedAt",
                table: "YearEndReviewItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QueryRaisedByUserId",
                table: "YearEndReviewItems",
                type: "TEXT",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "QueryResolvedAt",
                table: "YearEndReviewItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QueryResolvedByUserId",
                table: "YearEndReviewItems",
                type: "TEXT",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "QueryRespondedAt",
                table: "YearEndReviewItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QueryRespondedByUserId",
                table: "YearEndReviewItems",
                type: "TEXT",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QueryResponse",
                table: "YearEndReviewItems",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QueryAssignedToUserId",
                table: "YearEndReviewItems");

            migrationBuilder.DropColumn(
                name: "QueryDueDate",
                table: "YearEndReviewItems");

            migrationBuilder.DropColumn(
                name: "QueryRaisedAt",
                table: "YearEndReviewItems");

            migrationBuilder.DropColumn(
                name: "QueryRaisedByUserId",
                table: "YearEndReviewItems");

            migrationBuilder.DropColumn(
                name: "QueryResolvedAt",
                table: "YearEndReviewItems");

            migrationBuilder.DropColumn(
                name: "QueryResolvedByUserId",
                table: "YearEndReviewItems");

            migrationBuilder.DropColumn(
                name: "QueryRespondedAt",
                table: "YearEndReviewItems");

            migrationBuilder.DropColumn(
                name: "QueryRespondedByUserId",
                table: "YearEndReviewItems");

            migrationBuilder.DropColumn(
                name: "QueryResponse",
                table: "YearEndReviewItems");
        }
    }
}
