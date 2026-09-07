using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReadingTracker.Library.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "library");

            migrationBuilder.CreateTable(
                name: "LibraryEntries",
                schema: "library",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReaderId = table.Column<string>(type: "text", nullable: false),
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    TrackingMethod = table.Column<string>(type: "text", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryEntries_ReaderId_BookId",
                schema: "library",
                table: "LibraryEntries",
                columns: new[] { "ReaderId", "BookId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LibraryEntries",
                schema: "library");
        }
    }
}
