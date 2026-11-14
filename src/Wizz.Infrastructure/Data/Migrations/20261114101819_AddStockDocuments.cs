using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wizz.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStockDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DocumentYearDigits",
                table: "Stores",
                type: "INTEGER",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.AddColumn<bool>(
                name: "InventoryApplied",
                table: "Sales",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "DocumentSequences",
                columns: table => new
                {
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentType = table.Column<int>(type: "INTEGER", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    LastNumber = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentSequences", x => new { x.StoreId, x.DocumentType, x.Year });
                    table.ForeignKey(
                        name: "FK_DocumentSequences_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    DocumentDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ReferenceSaleId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    IsVoided = table.Column<bool>(type: "INTEGER", nullable: false),
                    VoidedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    VoidedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VoidReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    LegacyStockMovementId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockDocuments_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockDocumentLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StockDocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UnitId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    BaseQuantityDelta = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    QuantityBefore = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    QuantityAfter = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockDocumentLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockDocumentLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDocumentLines_StockDocuments_StockDocumentId",
                        column: x => x.StockDocumentId,
                        principalTable: "StockDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StockDocumentLines_UnitsOfMeasure_UnitId",
                        column: x => x.UnitId,
                        principalTable: "UnitsOfMeasure",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentLines_ProductId",
                table: "StockDocumentLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentLines_StockDocumentId",
                table: "StockDocumentLines",
                column: "StockDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentLines_UnitId",
                table: "StockDocumentLines",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocuments_LegacyStockMovementId",
                table: "StockDocuments",
                column: "LegacyStockMovementId",
                unique: true,
                filter: "[LegacyStockMovementId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocuments_ReferenceSaleId",
                table: "StockDocuments",
                column: "ReferenceSaleId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocuments_StoreId_DocumentDate",
                table: "StockDocuments",
                columns: new[] { "StoreId", "DocumentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StockDocuments_StoreId_Number",
                table: "StockDocuments",
                columns: new[] { "StoreId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentSequences");

            migrationBuilder.DropTable(
                name: "StockDocumentLines");

            migrationBuilder.DropTable(
                name: "StockDocuments");

            migrationBuilder.DropColumn(
                name: "DocumentYearDigits",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "InventoryApplied",
                table: "Sales");
        }
    }
}
