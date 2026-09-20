using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

/// <summary>
/// Reading Hardcover's export: its columns, its quoting (a review is JSON in a quoted cell,
/// commas and doubled quotes and all), and its words for a status, into what this shelf keeps.
/// </summary>
public class HardcoverExportTests
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

        var book = Assert.Single(HardcoverExport.Parse(text));

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

        var book = Assert.Single(HardcoverExport.Parse(text));

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

        Assert.Equal(status, Assert.Single(HardcoverExport.Parse(text)).Status);
    }

    [Fact]
    public void Falls_back_to_the_isbn_10_and_then_to_none()
    {
        var text = Header + "\n"
            + Row("Ten", "A", "Read", "015603008X", "", "", "") + "\n"
            + Row("None", "A", "Read", "", "", "", "");

        var books = HardcoverExport.Parse(text);

        Assert.Equal("015603008X", books[0].Isbn);
        Assert.Null(books[1].Isbn);
    }

    [Fact]
    public void Splits_several_authors_and_skips_a_row_with_no_title()
    {
        var text = Header + "\n"
            + Row("Good Omens", "\"Terry Pratchett, Neil Gaiman\"", "Read", "", "9780060853983", "", "") + "\n"
            + Row("", "Nobody", "Read", "", "", "", "") + "\n";

        var book = Assert.Single(HardcoverExport.Parse(text));

        Assert.Equal(["Terry Pratchett", "Neil Gaiman"], book.Authors);
    }

    [Fact]
    public void A_file_that_is_not_a_hardcover_export_is_said_to_be_one()
    {
        var problem = Assert.Throws<NotAHardcoverExport>(() => HardcoverExport.Parse("Name,Age\nBob,3"));

        Assert.Contains("Title", problem.Message);
    }
}
