using System.Globalization;

namespace ReadingTracker.Web.Services;

/// <summary>
/// Goodreads' library export, the one layout StoryGraph, Hardcover and Goodreads all take in:
/// its columns, in its order, and its words for a shelf. Reading a Goodreads file
/// (<see cref="LibraryExport.Goodreads"/>) and writing one both say them from here, so what is
/// written is what is read back.
/// </summary>
public static class GoodreadsCsv
{
    public const string Author = "Author";
    public const string Isbn10 = "ISBN";
    public const string Isbn13 = "ISBN13";
    public const string Pages = "Number of Pages";
    public const string DateRead = "Date Read";
    public const string ExclusiveShelf = "Exclusive Shelf";

    /// <summary>
    /// Each ReadingStatus and the shelf it goes on. Goodreads keeps three shelves of its own;
    /// paused and did-not-finish are shelves a reader makes, named as each site's import takes
    /// On hold and Dropped.
    /// </summary>
    private static readonly IReadOnlyList<(string Status, string Shelf)> Shelves =
    [
        ("WantToRead", "to-read"),
        ("Reading", "currently-reading"),
        ("Finished", "read"),
        ("OnHold", "paused"),
        ("Dropped", "did-not-finish"),
    ];

    /// <summary>
    /// Every column, as Goodreads writes its header today, and what this shelf puts in it. A site
    /// knows the file by the header; the columns the shelf has nothing for stay empty.
    /// </summary>
    private static readonly IReadOnlyList<(string Name, Func<Row, string> Cell)> Columns =
    [
        ("Book Id", Blank),
        ("Title", row => row.Book.Title),
        (Author, row => row.Book.Authors.FirstOrDefault() ?? ""),
        ("Author l-f", Blank),
        ("Additional Authors", row => string.Join(", ", row.Book.Authors.Skip(1))),
        (Isbn10, row => Formula(Isbn.TenOf(row.Book.Isbn))),
        (Isbn13, row => Formula(Isbn.ThirteenOf(row.Book.Isbn))),
        ("My Rating", Blank),
        ("Average Rating", Blank),
        ("Publisher", Blank),
        ("Binding", Blank),
        (Pages, row => row.Entry.EffectivePageCount?.ToString(CultureInfo.InvariantCulture) ?? ""),
        ("Year Published", Blank),
        ("Original Publication Year", Blank),
        (DateRead, row => ShelfOrder.FinishedOn(row.Entry) is { } finished ? Day(finished) : ""),
        ("Date Added", row => Day(ReaderDays.Of(row.Entry.AddedAt, row.Zone))),
        ("Bookshelves", row => ShelfOf(row.Entry.Status)),
        ("Bookshelves with positions", Blank),
        (ExclusiveShelf, row => ShelfOf(row.Entry.Status)),
        ("My Review", Blank),
        ("Spoiler", Blank),
        ("Private Notes", Blank),
        ("Read Count", Blank),
        ("Owned Copies", Blank),
    ];

    public static IReadOnlyList<string> Header { get; } = [.. Columns.Select(column => column.Name)];

    /// <summary>
    /// The shelf as Goodreads' export: the header, then a row for each entry. Only entries Catalog
    /// has described come here; a book with no title is no row at all to the sites that read it.
    /// </summary>
    public static string Write(IEnumerable<(LibraryEntry Entry, BookDetails Book)> shelf, TimeZoneInfo zone) =>
        Csv.Write([Header, .. shelf.Select(onShelf => Columns.Select(column => column.Cell(new Row(onShelf.Entry, onShelf.Book, zone))))]);

    /// <summary>The ReadingStatus a shelf of this name holds, or null for any other shelf.</summary>
    public static string? StatusOn(string shelf) =>
        Shelves.FirstOrDefault(pair => pair.Shelf == shelf).Status;

    private static string ShelfOf(string status) =>
        Shelves.FirstOrDefault(pair => pair.Status == status).Shelf ?? "to-read";

    /// <summary>Goodreads writes an ISBN as a spreadsheet formula, so a sheet keeps its leading zero and X.</summary>
    private static string Formula(string? isbn) => $"=\"{isbn}\"";

    private static string Day(DateOnly day) => day.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

    private static string Blank(Row row) => "";

    private sealed record Row(LibraryEntry Entry, BookDetails Book, TimeZoneInfo Zone);
}
