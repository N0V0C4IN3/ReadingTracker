using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReadingTracker.Library.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialLibrarySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Library owns this schema and nothing else writes to it (ADR-0004).
            migrationBuilder.EnsureSchema(name: "library");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty: the migrations history table lives in this schema, so
            // dropping it here would fail and destroy the record of applied migrations.
        }
    }
}
