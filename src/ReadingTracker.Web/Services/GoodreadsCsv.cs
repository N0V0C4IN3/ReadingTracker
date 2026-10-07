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
    public const string Title = "Title";
    public const string Author = "Author";
    public const string AdditionalAuthors = "Additional Authors";
    public const string Isbn = "ISBN";
    public const string Isbn13 = "ISBN13";
    public const string Pages = "Number of Pages";
    public const string DateRead = "Date Read";
    public const string DateAdded = "Date Added";
    public const string Bookshelves = "Bookshelves";
    public const string ExclusiveShelf = "Exclusive Shelf";

    /// <summary>Every column, as Goodreads writes its header today. A site knows the file by it.</summary>
    public static readonly IReadOnlyList<string> Header =
    [
        "Book Id", Title, Author, "Author l-f", AdditionalAuthors, Isbn, Isbn13, "My Rating", "Average Rating",
        "Publisher", "Binding", Pages, "Year Published", "Original Publication Year", DateRead, DateAdded,
        Bookshelves, "Bookshelves with positions", ExclusiveShelf, "My Review", "Spoiler", "Private Notes",
        "Read Count", "Owned Copies",
    ];

    /// <summary>Goodreads' three shelves of its own.</summary>
    public const string Read = "read";
    public const string CurrentlyReading = "currently-reading";
    public const string ToRead = "to-read";

    /// <summary>
    /// Shelves a reader makes for themselves. These are the names each site's import, and this
    /// one's, takes as On hold and Dropped.
    /// </summary>
    public const string Paused = "paused";
    public const string DidNotFinish = "did-not-finish";

    /// <summary>The shelf as Goodreads' export: the header, then a row for each entry.</summary>
    public static string Write(IEnumerable<LibraryEntry> entries, TimeZoneInfo zone)
    {
        var text = new System.Text.StringBuilder();
        Line(text, Header);

        foreach (var entry in entries)
        {
            if (entry.Book is not { } book)
            {
                continue;
            }

            var shelf = ShelfOf(entry.Status);
            var (isbn10, isbn13) = IsbnsOf(book.Isbn);
            var cells = new Dictionary<string, string>
            {
                [Title] = book.Title,
                [Author] = book.Authors.FirstOrDefault() ?? "",
                [AdditionalAuthors] = string.Join(", ", book.Authors.Skip(1)),
                [Isbn] = Formula(isbn10),
                [Isbn13] = Formula(isbn13),
                [Pages] = entry.EffectivePageCount?.ToString(CultureInfo.InvariantCulture) ?? "",
                [DateRead] = entry.Status == "Finished" && entry.FinishedOn is { } finished ? Day(finished) : "",
                [DateAdded] = Day(ReaderDays.Of(entry.AddedAt, zone)),
                [Bookshelves] = shelf,
                [ExclusiveShelf] = shelf,
            };

            Line(text, Header.Select(column => cells.GetValueOrDefault(column, "")));
        }

        return text.ToString();
    }

    private static string ShelfOf(string status) => status switch
    {
        "Reading" => CurrentlyReading,
        "Finished" => Read,
        "OnHold" => Paused,
        "Dropped" => DidNotFinish,
        _ => ToRead,
    };

    /// <summary>
    /// The Catalog's ISBN in both forms, so a site matching on either finds the edition. Every
    /// ISBN-10 has an ISBN-13, under 978; only a 978 ISBN-13 has an ISBN-10. Anything that is
    /// not an ISBN fills neither.
    /// </summary>
    private static (string? Ten, string? Thirteen) IsbnsOf(string? isbn)
    {
        var said = isbn?.Replace("-", "").Replace(" ", "").ToUpperInvariant() ?? "";

        if (said.Length == 10 && said[..9].All(char.IsAsciiDigit) && (char.IsAsciiDigit(said[9]) || said[9] == 'X'))
        {
            var body = "978" + said[..9];
            return (said, body + Check13(body));
        }

        if (said.Length == 13 && said.All(char.IsAsciiDigit))
        {
            return (said.StartsWith("978", StringComparison.Ordinal) ? said[3..12] + Check10(said[3..12]) : null, said);
        }

        return (null, null);
    }

    /// <summary>Weights 1 and 3 in turn; the check brings the sum to a multiple of ten.</summary>
    private static char Check13(string twelve)
    {
        var sum = twelve.Select((digit, at) => (digit - '0') * (at % 2 == 0 ? 1 : 3)).Sum();
        return (char)('0' + (10 - sum % 10) % 10);
    }

    /// <summary>Weights 10 down to 2; the check brings the sum to a multiple of eleven, and ten is X.</summary>
    private static char Check10(string nine)
    {
        var sum = nine.Select((digit, at) => (digit - '0') * (10 - at)).Sum();
        var check = (11 - sum % 11) % 11;
        return check == 10 ? 'X' : (char)('0' + check);
    }

    /// <summary>Goodreads writes an ISBN as a spreadsheet formula, so a sheet keeps its leading zero and X.</summary>
    private static string Formula(string? isbn) => $"=\"{isbn}\"";

    private static string Day(DateOnly day) => day.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// RFC 4180, as Goodreads writes it: a cell holding a comma, a quote or a line break is
    /// quoted, its quotes doubled, and lines end in CRLF.
    /// </summary>
    private static void Line(System.Text.StringBuilder text, IEnumerable<string> cells) =>
        text.Append(string.Join(',', cells.Select(Quoted))).Append("\r\n");

    private static string Quoted(string cell) =>
        cell.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? $"\"{cell.Replace("\"", "\"\"")}\"" : cell;
}
