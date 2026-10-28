using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wizz.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameCorrectionReasonToModificationNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CorrectionReason",
                table: "Sales",
                newName: "ModificationNote");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ModificationNote",
                table: "Sales",
                newName: "CorrectionReason");
        }
    }
}
