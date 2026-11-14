using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wizz.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCashierShiftsAndSaleAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "Sales",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "Sales",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "Sales",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModificationReason",
                table: "Sales",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedAt",
                table: "Sales",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ModifiedByUserId",
                table: "Sales",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ShiftId",
                table: "Sales",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CashierShifts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CashierId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OpenedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClosedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SalesTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CashTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreditTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashierShifts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashierShifts_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_ShiftId",
                table: "Sales",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_CashierShifts_OpenedAt",
                table: "CashierShifts",
                column: "OpenedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CashierShifts_StoreId_CashierId",
                table: "CashierShifts",
                columns: new[] { "StoreId", "CashierId" },
                unique: true,
                filter: "[ClosedAt] IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_CashierShifts_ShiftId",
                table: "Sales",
                column: "ShiftId",
                principalTable: "CashierShifts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sales_CashierShifts_ShiftId",
                table: "Sales");

            migrationBuilder.DropTable(
                name: "CashierShifts");

            migrationBuilder.DropIndex(
                name: "IX_Sales_ShiftId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ModificationReason",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ModifiedAt",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ModifiedByUserId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ShiftId",
                table: "Sales");
        }
    }
}
