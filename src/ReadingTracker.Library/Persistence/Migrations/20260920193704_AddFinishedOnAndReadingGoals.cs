using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReadingTracker.Library.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinishedOnAndReadingGoals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "FinishedOn",
                schema: "library",
                table: "LibraryEntries",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReadingGoals",
                schema: "library",
                columns: table => new
                {
                    ReaderId = table.Column<string>(type: "text", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Books = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingGoals", x => new { x.ReaderId, x.Year });
                });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryEntries_ReaderId_FinishedOn",
                schema: "library",
                table: "LibraryEntries",
                columns: new[] { "ReaderId", "FinishedOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReadingGoals",
                schema: "library");

            migrationBuilder.DropIndex(
                name: "IX_LibraryEntries_ReaderId_FinishedOn",
                schema: "library",
                table: "LibraryEntries");

            migrationBuilder.DropColumn(
                name: "FinishedOn",
                schema: "library",
                table: "LibraryEntries");
        }
    }
}
