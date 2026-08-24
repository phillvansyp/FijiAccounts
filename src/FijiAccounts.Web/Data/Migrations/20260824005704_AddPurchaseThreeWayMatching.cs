using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseThreeWayMatching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MatchApprovalReason",
                table: "PurchaseOrders",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MatchApprovedAt",
                table: "PurchaseOrders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MatchApprovedByUserId",
                table: "PurchaseOrders",
                type: "TEXT",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MatchEvaluatedAt",
                table: "PurchaseOrders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MatchFingerprint",
                table: "PurchaseOrders",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MatchPriceVariance",
                table: "PurchaseOrders",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MatchQuantityVariance",
                table: "PurchaseOrders",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "MatchStatus",
                table: "PurchaseOrders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "MatchSummary",
                table: "PurchaseOrders",
                type: "TEXT",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "MatchTotalVariance",
                table: "PurchaseOrders",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PurchasePriceTolerancePercent",
                table: "Organisations",
                type: "TEXT",
                precision: 8,
                scale: 4,
                nullable: false,
                defaultValue: 2m);

            migrationBuilder.AddColumn<decimal>(
                name: "PurchaseQuantityTolerancePercent",
                table: "Organisations",
                type: "TEXT",
                precision: 8,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PurchaseTotalToleranceAmount",
                table: "Organisations",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 5m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MatchApprovalReason",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "MatchApprovedAt",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "MatchApprovedByUserId",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "MatchEvaluatedAt",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "MatchFingerprint",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "MatchPriceVariance",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "MatchQuantityVariance",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "MatchStatus",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "MatchSummary",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "MatchTotalVariance",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "PurchasePriceTolerancePercent",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "PurchaseQuantityTolerancePercent",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "PurchaseTotalToleranceAmount",
                table: "Organisations");
        }
    }
}
