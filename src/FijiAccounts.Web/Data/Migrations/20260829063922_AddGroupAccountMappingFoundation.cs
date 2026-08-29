using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FijiAccounts.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupAccountMappingFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GroupLedgerAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationGroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Purpose = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupLedgerAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupLedgerAccounts_OrganisationGroups_OrganisationGroupId",
                        column: x => x.OrganisationGroupId,
                        principalTable: "OrganisationGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IntercompanyAccountConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationGroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CounterpartyOrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReceivableAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayableAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RevenueAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExpenseAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntercompanyAccountConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntercompanyAccountConfigurations_LedgerAccounts_ExpenseAccountId",
                        column: x => x.ExpenseAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IntercompanyAccountConfigurations_LedgerAccounts_PayableAccountId",
                        column: x => x.PayableAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IntercompanyAccountConfigurations_LedgerAccounts_ReceivableAccountId",
                        column: x => x.ReceivableAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IntercompanyAccountConfigurations_LedgerAccounts_RevenueAccountId",
                        column: x => x.RevenueAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IntercompanyAccountConfigurations_OrganisationGroups_OrganisationGroupId",
                        column: x => x.OrganisationGroupId,
                        principalTable: "OrganisationGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IntercompanyAccountConfigurations_Organisations_CounterpartyOrganisationId",
                        column: x => x.CounterpartyOrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IntercompanyAccountConfigurations_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GroupLedgerAccountMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationGroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LedgerAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GroupLedgerAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupLedgerAccountMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupLedgerAccountMappings_GroupLedgerAccounts_GroupLedgerAccountId",
                        column: x => x.GroupLedgerAccountId,
                        principalTable: "GroupLedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroupLedgerAccountMappings_LedgerAccounts_LedgerAccountId",
                        column: x => x.LedgerAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroupLedgerAccountMappings_OrganisationGroups_OrganisationGroupId",
                        column: x => x.OrganisationGroupId,
                        principalTable: "OrganisationGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroupLedgerAccountMappings_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroupLedgerAccountMappings_GroupLedgerAccountId",
                table: "GroupLedgerAccountMappings",
                column: "GroupLedgerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupLedgerAccountMappings_LedgerAccountId",
                table: "GroupLedgerAccountMappings",
                column: "LedgerAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupLedgerAccountMappings_OrganisationGroupId_OrganisationId",
                table: "GroupLedgerAccountMappings",
                columns: new[] { "OrganisationGroupId", "OrganisationId" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupLedgerAccountMappings_OrganisationId",
                table: "GroupLedgerAccountMappings",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupLedgerAccounts_OrganisationGroupId_Code",
                table: "GroupLedgerAccounts",
                columns: new[] { "OrganisationGroupId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntercompanyAccountConfigurations_CounterpartyOrganisationId",
                table: "IntercompanyAccountConfigurations",
                column: "CounterpartyOrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_IntercompanyAccountConfigurations_ExpenseAccountId",
                table: "IntercompanyAccountConfigurations",
                column: "ExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_IntercompanyAccountConfigurations_OrganisationGroupId_OrganisationId_CounterpartyOrganisationId",
                table: "IntercompanyAccountConfigurations",
                columns: new[] { "OrganisationGroupId", "OrganisationId", "CounterpartyOrganisationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntercompanyAccountConfigurations_OrganisationId",
                table: "IntercompanyAccountConfigurations",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_IntercompanyAccountConfigurations_PayableAccountId",
                table: "IntercompanyAccountConfigurations",
                column: "PayableAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_IntercompanyAccountConfigurations_ReceivableAccountId",
                table: "IntercompanyAccountConfigurations",
                column: "ReceivableAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_IntercompanyAccountConfigurations_RevenueAccountId",
                table: "IntercompanyAccountConfigurations",
                column: "RevenueAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupLedgerAccountMappings");

            migrationBuilder.DropTable(
                name: "IntercompanyAccountConfigurations");

            migrationBuilder.DropTable(
                name: "GroupLedgerAccounts");
        }
    }
}
