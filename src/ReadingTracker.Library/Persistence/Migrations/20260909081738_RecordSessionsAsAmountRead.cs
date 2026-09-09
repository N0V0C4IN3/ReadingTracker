using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReadingTracker.Library.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordSessionsAsAmountRead : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sessions record how much was read rather than which stretch it covered (ADR-0010).
            // A session that already exists said both, so its amount is the distance it spanned.
            //
            // This is not lossless, and cannot be: a reader who logged 1→40 and then 200→260 had
            // a position of 260, but read 99 pages. 99 is what those two sessions actually record,
            // so 99 is what they become — the alternative would be to invent reading that was
            // never logged.
            migrationBuilder.AddColumn<decimal>(
                name: "Amount",
                schema: "library",
                table: "ReadingSessions",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql(
                """
                UPDATE library."ReadingSessions"
                SET "Amount" = GREATEST("EndPosition" - "StartPosition", 0);
                """);

            migrationBuilder.DropColumn(
                name: "StartPosition",
                schema: "library",
                table: "ReadingSessions");

            migrationBuilder.DropColumn(
                name: "EndPosition",
                schema: "library",
                table: "ReadingSessions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "StartPosition",
                schema: "library",
                table: "ReadingSessions",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "EndPosition",
                schema: "library",
                table: "ReadingSessions",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            // Where a session started was never recorded once amounts replaced spans, so going
            // back can only put every session at the start of the book. Nothing is lost that was
            // still being kept; the reader's totals survive as the spans 0 → amount.
            migrationBuilder.Sql(
                """
                UPDATE library."ReadingSessions"
                SET "EndPosition" = "Amount";
                """);

            migrationBuilder.DropColumn(
                name: "Amount",
                schema: "library",
                table: "ReadingSessions");
        }
    }
}
