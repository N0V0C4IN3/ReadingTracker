using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

/// <summary>
/// What exporting the shelf comes to: the whole shelf as a file, or a reason there is none. Never
/// half a shelf — a reader must not take a file missing books for all of them.
/// </summary>
public class ShelfFileTests
{
    private static readonly TimeZoneInfo PlusThree = TimeZoneInfo.CreateCustomTimeZone("UTC+3", TimeSpan.FromHours(3), "UTC+3", "UTC+3");
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 22, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_shelf_with_its_books_is_a_file_named_for_the_readers_day()
    {
        var file = Assert.IsType<ShelfFile.Ready>(ShelfFile.Of([OnShelf("Stoner")], null, Now, PlusThree));

        Assert.Equal("readingtracker-library-2026-10-07.csv", file.Name);
        Assert.Equal("Stoner", Assert.Single(LibraryExport.Parse(file.Text).Books).Title);
    }

    [Fact]
    public void An_empty_shelf_has_nothing_to_export()
    {
        Assert.IsType<ShelfFile.Empty>(ShelfFile.Of([], null, Now, PlusThree));
    }

    [Fact]
    public void A_shelf_that_could_not_be_had_is_not_loaded()
    {
        Assert.IsType<ShelfFile.NotLoaded>(ShelfFile.Of(null, LibraryUnavailable.GatewayUnreachable, Now, PlusThree));
    }

    [Fact]
    public void A_reader_whose_session_ended_is_signed_out()
    {
        Assert.IsType<ShelfFile.SignedOut>(ShelfFile.Of(null, LibraryUnavailable.NotSignedIn, Now, PlusThree));
    }

    /// <summary>
    /// Library sends every entry even when Catalog cannot describe them, just without their books.
    /// One such entry means the shelf did not load whole, so no file is made of the rest.
    /// </summary>
    [Fact]
    public void A_shelf_with_any_book_missing_its_details_is_not_loaded()
    {
        var shelf = new[] { OnShelf("Stoner"), OnShelf("Lost") with { Book = null } };

        Assert.IsType<ShelfFile.NotLoaded>(ShelfFile.Of(shelf, null, Now, PlusThree));
    }

    private static LibraryEntry OnShelf(string title) =>
        new(
            Id: Guid.NewGuid(),
            BookId: Guid.NewGuid(),
            Status: "WantToRead",
            TrackingMethod: "Pages",
            AddedAt: Now,
            FinishedOn: null,
            PageCountOverride: null,
            EffectivePageCount: null,
            Book: new BookDetails(title, ["An Author"], null, CoverUrl: null, TotalPages: null),
            Progress: null,
            Bookmark: null);
}
