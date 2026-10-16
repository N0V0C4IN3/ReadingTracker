using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReadingTracker.Library.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Quotes",
                schema: "library",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LibraryEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    HighlightId = table.Column<string>(type: "text", nullable: false),
                    Passage = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    Percent = table.Column<decimal>(type: "numeric", nullable: false),
                    Chapter = table.Column<string>(type: "text", nullable: true),
                    SavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Quotes_LibraryEntries_LibraryEntryId",
                        column: x => x.LibraryEntryId,
                        principalSchema: "library",
                        principalTable: "LibraryEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_LibraryEntryId_HighlightId",
                schema: "library",
                table: "Quotes",
                columns: new[] { "LibraryEntryId", "HighlightId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Quotes",
                schema: "library");
        }
    }
}
