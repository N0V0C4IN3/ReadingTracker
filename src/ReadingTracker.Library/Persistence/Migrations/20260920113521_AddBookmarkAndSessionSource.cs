using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReadingTracker.Library.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookmarkAndSessionSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every session so far was typed in by its reader: devices did not exist yet.
            migrationBuilder.AddColumn<string>(
                name: "Source",
                schema: "library",
                table: "ReadingSessions",
                type: "text",
                nullable: false,
                defaultValue: "Reader");

            migrationBuilder.AddColumn<decimal>(
                name: "BookmarkPercent",
                schema: "library",
                table: "LibraryEntries",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BookmarkReportedAt",
                schema: "library",
                table: "LibraryEntries",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Source",
                schema: "library",
                table: "ReadingSessions");

            migrationBuilder.DropColumn(
                name: "BookmarkPercent",
                schema: "library",
                table: "LibraryEntries");

            migrationBuilder.DropColumn(
                name: "BookmarkReportedAt",
                schema: "library",
                table: "LibraryEntries");
        }
    }
}
