using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReadingTracker.Catalog.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalogSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Catalog owns this schema and nothing else writes to it (ADR-0004).
            migrationBuilder.EnsureSchema(name: "catalog");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSchema(name: "catalog");
        }
    }
}
