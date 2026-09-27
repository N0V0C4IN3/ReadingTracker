using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

/// <summary>
/// Reading the exports of Hardcover, Goodreads and StoryGraph: their columns, their quoting (a
/// Hardcover review is JSON in a quoted cell, commas and doubled quotes and all), and their words
/// for a status, into what this shelf keeps — and telling the three apart by their columns.
/// </summary>
public class LibraryExportTests
{
    private const string Header =
        "Title,Author,Series,Status,Privacy,Hardcover Book ID,Hardcover Edition ID,ISBN 10,ISBN 13,ASIN,Media,Country Code,Language Code,Binding,Pages,Duration in Seconds,Publish Date,Publisher,Genres,Moods,Tags,Content Warnings,Lists,Date Added,Date Started,Date Finished,Rating,Review,Review Contains Spoilers,Sponsored Review,Review Date,Review URL,Review Media URL,Private Notes,Owned,Compilation,Review Slate";

    private const string Slate =
        "\"{\"\"document\"\"=>{\"\"object\"\"=>\"\"document\"\", \"\"children\"\"=>[{\"\"text\"\"=>\"\"\"\", \"\"object\"\"=>\"\"text\"\"}]}}\"";

    private static string Row(string title, string author, string status, string isbn10, string isbn13, string pages, string finished, string genres = "") =>
        $"{title},{author},,{status},Public,480139,30399194,{isbn10},{isbn13},,Book,us,en,,{pages},,2004-06-14,Houghton Mifflin Harcourt,{genres},,,,Owned,2026-04-22,2026-04-28,{finished},4.0,\"\",false,false,,,,,true,No,{Slate}";

    [Fact]
    public void Reads_a_row_the_way_hardcover_writes_it()
    {
        var text = Header + "\n" + Row("Flowers for Algernon", "Daniel Keyes", "Read", "015603008X", "9780156030083", "311", "2026-05-15",
            genres: "\"[\"\"Fantasy\"\", \"\"Science Fiction\"\"]\"");

        var book = Assert.Single(Hardcover(text));

        Assert.Equal("Flowers for Algernon", book.Title);
        Assert.Equal(["Daniel Keyes"], book.Authors);
        Assert.Equal("9780156030083", book.Isbn);
        Assert.Equal(311, book.Pages);
        Assert.Equal("Finished", book.Status);
        Assert.Equal(new DateOnly(2026, 5, 15), book.FinishedOn);
    }

    [Fact]
    public void Reads_a_quoted_title_with_a_comma_and_a_quote_in_it()
    {
        var text = Header + "\n" + Row("\"The Hobbit, or \"\"There and Back Again\"\"\"", "J.R.R. Tolkien", "Read", "", "9780345339683", "310", "");

        var book = Assert.Single(Hardcover(text));

        Assert.Equal("The Hobbit, or \"There and Back Again\"", book.Title);
        Assert.Null(book.FinishedOn);
    }

    [Theory]
    [InlineData("Read", "Finished")]
    [InlineData("Currently Reading", "Reading")]
    [InlineData("Want to Read", "WantToRead")]
    [InlineData("Did Not Finish", "Dropped")]
    [InlineData("Paused", "OnHold")]
    [InlineData("Something New", "WantToRead")]
    public void Says_each_hardcover_status_in_this_shelfs_words(string hardcover, string status)
    {
        var text = Header + "\n" + Row("A Book", "An Author", hardcover, "", "9780345339683", "", "");

        Assert.Equal(status, Assert.Single(Hardcover(text)).Status);
    }

    [Fact]
    public void Falls_back_to_the_isbn_10_and_then_to_none()
    {
        var text = Header + "\n"
            + Row("Ten", "A", "Read", "015603008X", "", "", "") + "\n"
            + Row("None", "A", "Read", "", "", "", "");

        var books = Hardcover(text);

        Assert.Equal("015603008X", books[0].Isbn);
        Assert.Null(books[1].Isbn);
    }

    [Fact]
    public void Splits_several_authors_and_skips_a_row_with_no_title()
    {
        var text = Header + "\n"
            + Row("Good Omens", "\"Terry Pratchett, Neil Gaiman\"", "Read", "", "9780060853983", "", "") + "\n"
            + Row("", "Nobody", "Read", "", "", "", "") + "\n";

        var book = Assert.Single(Hardcover(text));

        Assert.Equal(["Terry Pratchett", "Neil Gaiman"], book.Authors);
    }

    [Fact]
    public void A_file_that_is_not_a_hardcover_export_is_said_to_be_one()
    {
        var problem = Assert.Throws<NotAnExport>(() => LibraryExport.Parse("Name,Age\nBob,3", LibraryExport.Hardcover));

        Assert.Contains("Hardcover", problem.Message);
        Assert.Contains("Title", problem.Message);
    }

    private static IReadOnlyList<ImportedBook> Hardcover(string text)
    {
        var (source, books) = LibraryExport.Parse(text, LibraryExport.Hardcover);
        Assert.Same(LibraryExport.Hardcover, source);
        return books;
    }

    // Goodreads ------------------------------------------------------------------------------

    private const string GoodreadsHeader =
        "Book Id,Title,Author,Author l-f,Additional Authors,ISBN,ISBN13,My Rating,Average Rating,Publisher,Binding,Number of Pages,Year Published,Original Publication Year,Date Read,Date Added,Bookshelves,Bookshelves with positions,Exclusive Shelf,My Review,Spoiler,Private Notes,Read Count,Owned Copies";

    private static string GoodreadsRow(string title, string author, string additional, string isbn, string isbn13, string pages, string dateRead, string shelf) =>
        $"1234,{title},{author},\"Keyes, Daniel\",{additional},\"=\"\"{isbn}\"\"\",\"=\"\"{isbn13}\"\"\",4,4.13,Mariner,Paperback,{pages},2005,1966,{dateRead},2026/04/22,,,{shelf},,,,1,0";

    [Fact]
    public void Reads_a_goodreads_row_and_unwraps_its_isbn_formulas()
    {
        var text = GoodreadsHeader + "\n" + GoodreadsRow("\"The Last Wish (The Witcher, #0.5)\"", "Andrzej Sapkowski", "Danusia Stok", "0316029181", "9780316029186", "360", "2025/08/09", "read");

        var (source, books) = LibraryExport.Parse(text, LibraryExport.Hardcover);
        var book = Assert.Single(books);

        Assert.Same(LibraryExport.Goodreads, source);
        Assert.Equal("The Last Wish", book.Title);
        Assert.Equal(["Andrzej Sapkowski", "Danusia Stok"], book.Authors);
        Assert.Equal("9780316029186", book.Isbn);
        Assert.Equal(360, book.Pages);
        Assert.Equal("Finished", book.Status);
        Assert.Equal(new DateOnly(2025, 8, 9), book.FinishedOn);
    }

    [Fact]
    public void A_goodreads_book_with_no_isbn_has_none()
    {
        var text = GoodreadsHeader + "\n" + GoodreadsRow("Stoner", "John Williams", "", "", "", "", "", "to-read");

        var book = Assert.Single(LibraryExport.Parse(text, LibraryExport.Goodreads).Books);

        Assert.Null(book.Isbn);
        Assert.Null(book.Pages);
        Assert.Null(book.FinishedOn);
    }

    [Theory]
    [InlineData("read", "Finished")]
    [InlineData("currently-reading", "Reading")]
    [InlineData("to-read", "WantToRead")]
    [InlineData("did-not-finish", "Dropped")]
    [InlineData("dnf", "Dropped")]
    [InlineData("on-hold", "OnHold")]
    [InlineData("favourites", "WantToRead")]
    public void Says_each_goodreads_shelf_in_this_shelfs_words(string shelf, string status)
    {
        var text = GoodreadsHeader + "\n" + GoodreadsRow("A Book", "An Author", "", "", "", "", "", shelf);

        Assert.Equal(status, Assert.Single(LibraryExport.Parse(text, LibraryExport.Goodreads).Books).Status);
    }

    // StoryGraph -----------------------------------------------------------------------------

    private const string StoryGraphHeader =
        "Title,Authors,Contributors,ISBN/UID,Format,Read Status,Date Added,Last Date Read,Dates Read,Read Count,Moods,Pace,Character- or Plot-Driven?,Strong Character Development?,Loveable Characters?,Diverse Characters?,Flawed Characters?,Star Rating,Review,Content Warnings,Content Warning Description,Tags,Owned?";

    private static string StoryGraphRow(string title, string authors, string isbn, string status, string lastRead) =>
        $"{title},{authors},,{isbn},paperback,{status},2026/04/22,{lastRead},,1,\"adventurous, funny\",fast,Plot,Yes,Yes,No,Yes,4.5,\"\",,,,No";

    [Fact]
    public void Reads_a_storygraph_row()
    {
        var text = StoryGraphHeader + "\n" + StoryGraphRow("Good Omens", "\"Terry Pratchett, Neil Gaiman\"", "9780060853983", "read", "2025/06/30");

        var (source, books) = LibraryExport.Parse(text, LibraryExport.Goodreads);
        var book = Assert.Single(books);

        Assert.Same(LibraryExport.StoryGraph, source);
        Assert.Equal("Good Omens", book.Title);
        Assert.Equal(["Terry Pratchett", "Neil Gaiman"], book.Authors);
        Assert.Equal("9780060853983", book.Isbn);
        Assert.Null(book.Pages);
        Assert.Equal("Finished", book.Status);
        Assert.Equal(new DateOnly(2025, 6, 30), book.FinishedOn);
    }

    [Fact]
    public void A_storygraph_id_that_is_not_an_isbn_is_not_taken_for_one()
    {
        var text = StoryGraphHeader + "\n" + StoryGraphRow("Piranesi", "Susanna Clarke", "5f2b1c9e-7d4a-4e1b", "to-read", "");

        Assert.Null(Assert.Single(LibraryExport.Parse(text, LibraryExport.StoryGraph).Books).Isbn);
    }

    [Theory]
    [InlineData("read", "Finished")]
    [InlineData("currently-reading", "Reading")]
    [InlineData("to-read", "WantToRead")]
    [InlineData("did-not-finish", "Dropped")]
    [InlineData("paused", "OnHold")]
    public void Says_each_storygraph_status_in_this_shelfs_words(string storyGraph, string status)
    {
        var text = StoryGraphHeader + "\n" + StoryGraphRow("A Book", "An Author", "", storyGraph, "");

        Assert.Equal(status, Assert.Single(LibraryExport.Parse(text, LibraryExport.StoryGraph).Books).Status);
    }
}
