using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReadingTracker.Catalog.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LookAgainAtDescriptionsFromSearches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A Google Books search's description is the volume's with its paragraph breaks gone,
            // and a Book cached from a search kept it as its details, so it was never asked for
            // the volume's own. Those are the Books whose details were recorded the moment they
            // were made; looked up later, the two times differ. Forgetting that they were looked
            // up has each asked for its volume the next time somebody opens it, which replaces
            // the run-together description with the paragraphs the publisher wrote.
            migrationBuilder.Sql(
                """
                UPDATE catalog."Books"
                SET "DetailsLookedAt" = NULL
                WHERE "Source" = 'GoogleBooks'
                  AND "DetailsLookedAt" = "CreatedAt"
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to put back: each Book is looked up again as it is opened, and what it had
            // before is what that lookup replaced.
        }
    }
}
