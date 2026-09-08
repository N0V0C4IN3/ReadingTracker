using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReadingTracker.Library.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPageCountOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PageCountOverride",
                schema: "library",
                table: "LibraryEntries",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PageCountOverride",
                schema: "library",
                table: "LibraryEntries");
        }
    }
}
