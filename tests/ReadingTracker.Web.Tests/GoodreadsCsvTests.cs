using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

/// <summary>
/// Writing the shelf as Goodreads' library export, which StoryGraph, Hardcover and Goodreads all
/// take in — and which this app's own import reads back.
/// </summary>
public class GoodreadsCsvTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    [Fact]
    public void The_first_line_is_goodreads_header_exactly()
    {
        var text = GoodreadsCsv.Write([], Utc);

        Assert.Equal(
            "Book Id,Title,Author,Author l-f,Additional Authors,ISBN,ISBN13,My Rating,Average Rating,Publisher,Binding,Number of Pages,Year Published,Original Publication Year,Date Read,Date Added,Bookshelves,Bookshelves with positions,Exclusive Shelf,My Review,Spoiler,Private Notes,Read Count,Owned Copies\r\n",
            text);
    }

    [Fact]
    public void A_finished_book_comes_back_through_the_import_as_it_went()
    {
        var entry = Entry("Flowers for Algernon", ["Daniel Keyes"], "9780156030083", 311, "Finished", new DateOnly(2026, 5, 15));

        var (source, books) = LibraryExport.Parse(GoodreadsCsv.Write([entry], Utc));
        var book = Assert.Single(books);

        Assert.Same(LibraryExport.Goodreads, source);
        Assert.Equal("Flowers for Algernon", book.Title);
        Assert.Equal(["Daniel Keyes"], book.Authors);
        Assert.Equal("9780156030083", book.Isbn);
        Assert.Equal(311, book.Pages);
        Assert.Equal("Finished", book.Status);
        Assert.Equal(new DateOnly(2026, 5, 15), book.FinishedOn);
    }

    /// <summary>
    /// A whole shelf, through the import and back. The import takes the first author only (it
    /// leaves Goodreads' translators out) and prefers the ISBN-13, so that is what returns.
    /// </summary>
    [Fact]
    public void A_shelf_of_every_status_comes_back_through_the_import()
    {
        var shelf = new[]
        {
            Entry("Stoner", ["John Williams"], "9781590171998", 278, "WantToRead"),
            Entry("The Hobbit, or \"There and Back Again\"", ["J.R.R. Tolkien"], "0345339681", 310, "Reading"),
            Entry("Good Omens", ["Terry Pratchett", "Neil Gaiman"], "9780060853983", 412, "Finished", new DateOnly(2025, 6, 30)),
            Entry("Poems\nCollected", ["An Author"], null, null, "OnHold", new DateOnly(2024, 2, 2)),
            Entry("Ulysses", ["James Joyce"], "9791032305690", 730, "Dropped"),
        };

        var (source, books) = LibraryExport.Parse(GoodreadsCsv.Write(shelf, Utc));

        Assert.Same(LibraryExport.Goodreads, source);
        Assert.Equal(
            [
                new ImportedBook("Stoner", ["John Williams"], "9781590171998", 278, "WantToRead", null),
                new ImportedBook("The Hobbit, or \"There and Back Again\"", ["J.R.R. Tolkien"], "9780345339683", 310, "Reading", null),
                new ImportedBook("Good Omens", ["Terry Pratchett"], "9780060853983", 412, "Finished", new DateOnly(2025, 6, 30)),
                new ImportedBook("Poems\nCollected", ["An Author"], null, null, "OnHold", null),
                new ImportedBook("Ulysses", ["James Joyce"], "9791032305690", 730, "Dropped", null),
            ],
            books,
            new BookComparer());
    }

    /// <summary>ImportedBook holds its authors in a list, which a record compares by reference.</summary>
    private sealed class BookComparer : IEqualityComparer<ImportedBook>
    {
        public bool Equals(ImportedBook? x, ImportedBook? y) =>
            x is not null && y is not null && x with { Authors = [] } == y with { Authors = [] } && x.Authors.SequenceEqual(y.Authors);

        public int GetHashCode(ImportedBook book) => book.Title.GetHashCode();
    }

    [Theory]
    [InlineData("WantToRead", "to-read")]
    [InlineData("Reading", "currently-reading")]
    [InlineData("Finished", "read")]
    [InlineData("OnHold", "paused")]
    [InlineData("Dropped", "did-not-finish")]
    public void Each_status_goes_on_its_shelf_and_comes_back_as_itself(string status, string shelf)
    {
        var entry = Entry("A Book", ["An Author"], null, null, status, new DateOnly(2026, 5, 15));

        var text = GoodreadsCsv.Write([entry], Utc);

        Assert.Equal(shelf, Cell(text, "Exclusive Shelf"));
        Assert.Equal(shelf, Cell(text, "Bookshelves"));
        Assert.Equal(status, Assert.Single(LibraryExport.Parse(text).Books).Status);
    }

    [Theory]
    [InlineData("Reading")]
    [InlineData("Dropped")]
    public void A_book_no_longer_finished_carries_no_read_date(string status)
    {
        var entry = Entry("A Book", ["An Author"], null, null, status, finishedOn: new DateOnly(2025, 1, 3));

        Assert.Equal("", Cell(GoodreadsCsv.Write([entry], Utc), "Date Read"));
    }

    [Theory]
    [InlineData("The Hobbit, or There and Back Again")]
    [InlineData("The \"Real\" Story")]
    [InlineData("Poems\nCollected")]
    [InlineData("Poems\r\nCollected")]
    public void A_title_with_a_comma_a_quote_or_a_line_break_comes_back_whole(string title)
    {
        var entries = new[]
        {
            Entry(title, ["An Author"], null, null, "Reading"),
            Entry("The Next One", ["Someone Else"], null, null, "WantToRead"),
        };

        var books = LibraryExport.Parse(GoodreadsCsv.Write(entries, Utc)).Books;

        Assert.Equal([title, "The Next One"], books.Select(book => book.Title));
        Assert.Equal(["An Author", "Someone Else"], books.Select(book => book.Authors.Single()));
    }

    [Fact]
    public void A_cell_with_a_comma_or_quote_is_quoted_with_its_quotes_doubled_and_lines_end_in_crlf()
    {
        var entry = Entry("Say \"Hi\", Then Go", ["An Author"], null, null, "WantToRead");

        var row = GoodreadsCsv.Write([entry], Utc).Split("\r\n")[1];

        Assert.StartsWith(",\"Say \"\"Hi\"\", Then Go\",An Author,", row);
    }

    [Fact]
    public void The_first_author_is_the_author_and_the_rest_are_additional()
    {
        var entry = Entry("Good Omens", ["Terry Pratchett", "Neil Gaiman", "Someone Third"], null, null, "Finished", new DateOnly(2025, 6, 30));

        var text = GoodreadsCsv.Write([entry], Utc);

        Assert.Contains(",Terry Pratchett,,\"Neil Gaiman, Someone Third\",", text);
        Assert.Equal(["Terry Pratchett"], Assert.Single(LibraryExport.Parse(text).Books).Authors);
    }

    /// <summary>The ISBN and ISBN13 cells side by side, as Goodreads writes them: formulas, quoted.</summary>
    [Theory]
    [InlineData("9780156030083", "\"=\"\"015603008X\"\"\",\"=\"\"9780156030083\"\"\"")]
    [InlineData("015603008X", "\"=\"\"015603008X\"\"\",\"=\"\"9780156030083\"\"\"")]
    [InlineData("978-0-316-02918-6", "\"=\"\"0316029181\"\"\",\"=\"\"9780316029186\"\"\"")]
    [InlineData("0316029181", "\"=\"\"0316029181\"\"\",\"=\"\"9780316029186\"\"\"")]
    [InlineData("9791032305690", "\"=\"\"\"\"\",\"=\"\"9791032305690\"\"\"")]
    [InlineData(null, "\"=\"\"\"\"\",\"=\"\"\"\"\"")]
    public void Both_isbn_columns_are_filled_where_one_gives_the_other(string? isbn, string cells)
    {
        var entry = Entry("A Book", ["An Author"], isbn, null, "WantToRead");

        Assert.Contains($",An Author,,,{cells},", GoodreadsCsv.Write([entry], Utc));
    }

    [Fact]
    public void An_isbn_10_comes_back_through_the_import_as_its_isbn_13()
    {
        var entry = Entry("Flowers for Algernon", ["Daniel Keyes"], "015603008X", null, "WantToRead");

        Assert.Equal("9780156030083", Assert.Single(LibraryExport.Parse(GoodreadsCsv.Write([entry], Utc)).Books).Isbn);
    }

    [Fact]
    public void Date_added_is_the_readers_own_day_and_both_dates_are_written_as_goodreads_writes_them()
    {
        var kyiv = TimeZoneInfo.CreateCustomTimeZone("UTC+3", TimeSpan.FromHours(3), "UTC+3", "UTC+3");
        var entry = Entry("A Book", ["An Author"], null, null, "Finished", new DateOnly(2026, 5, 5),
            addedAt: new DateTimeOffset(2026, 4, 1, 22, 30, 0, TimeSpan.Zero));

        var text = GoodreadsCsv.Write([entry], kyiv);

        Assert.Equal("2026/04/02", Cell(text, "Date Added"));
        Assert.Equal("2026/05/05", Cell(text, "Date Read"));
    }

    [Fact]
    public void Number_of_pages_is_the_effective_page_count()
    {
        var entry = Entry("A Book", ["An Author"], null, 412, "Reading") with { PageCountOverride = 388, EffectivePageCount = 388 };

        Assert.Equal("388", Cell(GoodreadsCsv.Write([entry], Utc), "Number of Pages"));
    }

    [Fact]
    public void A_book_entered_by_hand_with_no_isbn_or_page_count_is_still_exported()
    {
        var entry = Entry("Grandad's Notebooks", ["A. Relative"], null, null, "WantToRead");

        var book = Assert.Single(LibraryExport.Parse(GoodreadsCsv.Write([entry], Utc)).Books);

        Assert.Equal("Grandad's Notebooks", book.Title);
        Assert.Null(book.Isbn);
        Assert.Null(book.Pages);
    }

    [Fact]
    public void An_entry_whose_book_could_not_be_described_has_no_row()
    {
        var undescribed = Entry("Gone", ["Nobody"], null, null, "Reading") with { Book = null };

        Assert.Single(GoodreadsCsv.Write([undescribed], Utc).Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>One cell of the first book's row, by its column. Only for rows with no quoted cells.</summary>
    private static string Cell(string text, string column)
    {
        var lines = text.Split("\r\n");
        var at = Array.IndexOf(lines[0].Split(','), column);
        return lines[1].Split(',')[at];
    }

    private static LibraryEntry Entry(
        string title,
        IReadOnlyList<string> authors,
        string? isbn,
        int? pages,
        string status,
        DateOnly? finishedOn = null,
        DateTimeOffset? addedAt = null) =>
        new(
            Id: Guid.NewGuid(),
            BookId: Guid.NewGuid(),
            Status: status,
            TrackingMethod: "Pages",
            AddedAt: addedAt ?? new DateTimeOffset(2026, 4, 22, 9, 0, 0, TimeSpan.Zero),
            FinishedOn: finishedOn,
            PageCountOverride: null,
            EffectivePageCount: pages,
            Book: new BookDetails(title, authors, isbn, CoverUrl: null, TotalPages: pages),
            Progress: null,
            Bookmark: null);
}
