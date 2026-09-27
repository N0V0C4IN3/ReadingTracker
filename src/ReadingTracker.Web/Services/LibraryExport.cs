using System.Globalization;
using System.Text.RegularExpressions;

namespace ReadingTracker.Web.Services;

/// <summary>
/// One book as another site's export describes it, already in this shelf's words: the ISBN that
/// names the edition, the ReadingStatus, and — for a finished book — the day it was finished
/// when the site knew it.
/// </summary>
public sealed record ImportedBook(
    string Title,
    IReadOnlyList<string> Authors,
    string? Isbn,
    int? Pages,
    string Status,
    DateOnly? FinishedOn);

public sealed class NotAnExport(string message) : Exception(message);

/// <summary>
/// A shelf exported from another site as a CSV: Hardcover, Goodreads or StoryGraph. The three
/// differ only in what they call their columns, so each is just that — a name, how to get the
/// file, and which column holds what — and one reader turns any of them into books. What the
/// shelf has a place for comes out: title, author, ISBN, pages, status, the day finished.
/// Ratings, reviews, moods and the rest have no home here and stay behind.
/// </summary>
public sealed partial record LibraryExport(
    string Name,
    string? FileName,
    IReadOnlyList<string> Steps,
    string AuthorColumn,
    IReadOnlyList<string> IsbnColumns,
    string? PagesColumn,
    string StatusColumn,
    string FinishedColumn)
{
    public static readonly LibraryExport Hardcover = new(
        Name: "Hardcover",
        FileName: "hardcover-export-….csv",
        Steps: ["In Hardcover, open Settings.", "Choose Export and download the CSV."],
        AuthorColumn: "Author",
        IsbnColumns: ["ISBN 13", "ISBN 10"],
        PagesColumn: "Pages",
        StatusColumn: "Status",
        FinishedColumn: "Date Finished");

    /// <summary>
    /// Only Author, not Additional Authors: Goodreads puts translators, illustrators and editors
    /// there, and a book made by hand in the catalogue would list them as its writers.
    /// </summary>
    public static readonly LibraryExport Goodreads = new(
        Name: "Goodreads",
        FileName: "goodreads_library_export.csv",
        Steps:
        [
            "In Goodreads, open My Books.",
            "Under Tools, choose Import and export.",
            "Choose Export Library, wait for the link, then download it.",
        ],
        AuthorColumn: "Author",
        IsbnColumns: ["ISBN13", "ISBN"],
        PagesColumn: "Number of Pages",
        StatusColumn: "Exclusive Shelf",
        FinishedColumn: "Date Read");

    /// <summary>StoryGraph keeps no page count; the catalogue fills it in when it finds the book.</summary>
    public static readonly LibraryExport StoryGraph = new(
        Name: "StoryGraph",
        FileName: null,
        Steps:
        [
            "On the StoryGraph website — the app cannot — open your picture, then Manage Account.",
            "Under Manage Your Data, choose Export StoryGraph Library.",
            "Download the CSV when it is ready.",
        ],
        AuthorColumn: "Authors",
        IsbnColumns: ["ISBN/UID"],
        PagesColumn: null,
        StatusColumn: "Read Status",
        FinishedColumn: "Last Date Read");

    public static readonly IReadOnlyList<LibraryExport> All = [Hardcover, Goodreads, StoryGraph];

    /// <summary>The columns that tell this site's export from the others'.</summary>
    public IReadOnlyList<string> Needed => ["Title", AuthorColumn, StatusColumn];

    /// <summary>
    /// Reads a file as whichever of the three it turns out to be, and says which. The site the
    /// reader picked only chose the steps they were shown; the file is what they meant.
    /// </summary>
    public static (LibraryExport Source, IReadOnlyList<ImportedBook> Books) Parse(string text)
    {
        var rows = Csv.Read(text);
        if (rows.Count == 0)
        {
            throw new NotAnExport("The file is empty.");
        }

        var header = rows[0].Select(cell => cell.Trim()).ToList();
        var source = All.FirstOrDefault(export => export.Needed.All(header.Contains))
            ?? throw new NotAnExport($"This does not look like an export from {string.Join(", ", All.Select(export => export.Name).SkipLast(1))} or {All[^1].Name}.");

        var books = rows.Skip(1)
            .Select(cells => new Row(header, cells))
            .Where(row => row["Title"].Length > 0)
            .Select(source.Read)
            .ToList();

        return (source, books);
    }

    /// <summary>
    /// A finished book keeps its day; any other drops it. Sites leave the last finish on a book
    /// being read again, or given up on, and the shelf takes a day only with Finished.
    /// </summary>
    private ImportedBook Read(Row row)
    {
        var status = StatusOf(row[StatusColumn]);
        return new ImportedBook(
            SeriesMark().Replace(row["Title"], ""),
            [.. row[AuthorColumn].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
            IsbnColumns.Select(column => IsbnOf(row[column])).FirstOrDefault(isbn => isbn is not null),
            PagesColumn is { } pages && int.TryParse(row[pages], out var count) && count > 0 ? count : null,
            status,
            status == "Finished" ? DayOf(row[FinishedColumn]) : null);
    }

    /// <summary>
    /// The three sites' words for a status, one table: Hardcover's "Currently Reading" is
    /// StoryGraph's "currently-reading" once it is said the same way. Goodreads has three shelves
    /// of its own and lets a reader make more; a shelf they made is judged by the words in its
    /// name, whole words only, so "dnf" is given up but "household" is not on hold. Anything
    /// else is Want to read, which claims nothing.
    /// </summary>
    private static string StatusOf(string word)
    {
        var said = word.Trim().ToLowerInvariant().Replace(' ', '-').Replace('_', '-');
        var words = said.Split('-', StringSplitOptions.RemoveEmptyEntries);

        return said switch
        {
            "read" => "Finished",
            "currently-reading" => "Reading",
            _ when words.Intersect(["dnf", "abandoned", "dropped"]).Any() || (words.Contains("not") && words.Contains("finish")) => "Dropped",
            _ when words.Intersect(["paused", "hold"]).Any() => "OnHold",
            _ => "WantToRead",
        };
    }

    /// <summary>
    /// An ISBN, if the cell holds one. Goodreads writes its ISBNs as spreadsheet formulas —
    /// <c>="9780156030083"</c> — and StoryGraph's column holds its own ids for books without one.
    /// </summary>
    private static string? IsbnOf(string cell)
    {
        var isbn = cell.Replace("=", "").Replace("\"", "").Replace("-", "").Replace(" ", "").ToUpperInvariant();
        return IsbnShape().IsMatch(isbn) ? isbn : null;
    }

    /// <summary>Hardcover writes 2026-05-15, Goodreads 2026/05/15; these take either, padded or not.</summary>
    private static DateOnly? DayOf(string cell) =>
        DateOnly.TryParseExact(cell, ["yyyy-M-d", "yyyy/M/d"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    [GeneratedRegex(@"^(\d{9}[\dX]|\d{13})$")]
    private static partial Regex IsbnShape();

    /// <summary>Goodreads puts the series in the title — "The Last Wish (The Witcher, #0.5)" — which a search would trip on.</summary>
    [GeneratedRegex(@"\s*\([^()]*#[\d.]+\)\s*$")]
    private static partial Regex SeriesMark();

    private readonly record struct Row(List<string> Header, List<string> Cells)
    {
        public string this[string column]
        {
            get
            {
                var at = Header.IndexOf(column);
                return at >= 0 && at < Cells.Count ? Cells[at].Trim() : "";
            }
        }
    }

    /// <summary>RFC 4180, which is what all three write: quoted cells, doubled quotes, newlines inside quotes.</summary>
    private static class Csv
    {
        public static List<List<string>> Read(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new System.Text.StringBuilder();
            var quoted = false;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];

                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            cell.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        cell.Append(c);
                    }

                    continue;
                }

                switch (c)
                {
                    case '"':
                        quoted = true;
                        break;
                    case ',':
                        row.Add(cell.ToString());
                        cell.Clear();
                        break;
                    case '\r':
                        break;
                    case '\n':
                        row.Add(cell.ToString());
                        cell.Clear();
                        rows.Add(row);
                        row = [];
                        break;
                    default:
                        cell.Append(c);
                        break;
                }
            }

            if (cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString());
                rows.Add(row);
            }

            // A trailing newline leaves an empty last row; a blank line anywhere is not a book.
            return rows.Where(r => r.Count > 1 || (r.Count == 1 && r[0].Length > 0)).ToList();
        }
    }
}
