using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wizz.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ModifySaleModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "OriginalSaleId",
                table: "Sales",
                newName: "ModifiedByUserId");

            migrationBuilder.RenameColumn(
                name: "IsCorrected",
                table: "Sales",
                newName: "IsModified");

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedAt",
                table: "Sales",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ModifiedAt",
                table: "Sales");

            migrationBuilder.RenameColumn(
                name: "ModifiedByUserId",
                table: "Sales",
                newName: "OriginalSaleId");

            migrationBuilder.RenameColumn(
                name: "IsModified",
                table: "Sales",
                newName: "IsCorrected");
        }
    }
}
