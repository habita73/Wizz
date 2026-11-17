using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wizz.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCashDrawerAndPurchases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CashDifferenceTolerance",
                table: "Stores",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "CashDrawerEnabled",
                table: "Stores",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplierInvoiceNumber",
                table: "StockDocuments",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplierName",
                table: "StockDocuments",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LineCost",
                table: "StockDocumentLines",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCost",
                table: "StockDocumentLines",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CashDifference",
                table: "CashierShifts",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CloseNote",
                table: "CashierShifts",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CountedCash",
                table: "CashierShifts",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DepositsTotal",
                table: "CashierShifts",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedCash",
                table: "CashierShifts",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningFloat",
                table: "CashierShifts",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "WithdrawalsTotal",
                table: "CashierShifts",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CashMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShiftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReferencePurchaseId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashMovements_CashierShifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "CashierShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashMovements_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_OccurredAt",
                table: "CashMovements",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_ReferencePurchaseId",
                table: "CashMovements",
                column: "ReferencePurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_ShiftId",
                table: "CashMovements",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_StoreId",
                table: "CashMovements",
                column: "StoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashMovements");

            migrationBuilder.DropColumn(
                name: "CashDifferenceTolerance",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "CashDrawerEnabled",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "SupplierInvoiceNumber",
                table: "StockDocuments");

            migrationBuilder.DropColumn(
                name: "SupplierName",
                table: "StockDocuments");

            migrationBuilder.DropColumn(
                name: "LineCost",
                table: "StockDocumentLines");

            migrationBuilder.DropColumn(
                name: "UnitCost",
                table: "StockDocumentLines");

            migrationBuilder.DropColumn(
                name: "CashDifference",
                table: "CashierShifts");

            migrationBuilder.DropColumn(
                name: "CloseNote",
                table: "CashierShifts");

            migrationBuilder.DropColumn(
                name: "CountedCash",
                table: "CashierShifts");

            migrationBuilder.DropColumn(
                name: "DepositsTotal",
                table: "CashierShifts");

            migrationBuilder.DropColumn(
                name: "ExpectedCash",
                table: "CashierShifts");

            migrationBuilder.DropColumn(
                name: "OpeningFloat",
                table: "CashierShifts");

            migrationBuilder.DropColumn(
                name: "WithdrawalsTotal",
                table: "CashierShifts");
        }
    }
}
