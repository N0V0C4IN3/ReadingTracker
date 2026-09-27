using System.Globalization;
using System.Text.RegularExpressions;

namespace ReadingTracker.Web.Services;

/// <summary>
/// One book as another site's export describes it, already in this shelf's words: the ISBN that
/// names the edition, the ReadingStatus, and the day it was finished when the site knew it.
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
/// A shelf exported from another site as a CSV: Hardcover, Goodreads or StoryGraph. Each names
/// its columns and its statuses its own way; each subclass says only that, and what the shelf has
/// a place for — title, authors, ISBN, pages, status, the day finished — comes out the same.
/// Ratings, reviews, moods and the rest have no home here and stay behind.
/// </summary>
public abstract partial class LibraryExport
{
    public static readonly LibraryExport Hardcover = new HardcoverExport();
    public static readonly LibraryExport Goodreads = new GoodreadsExport();
    public static readonly LibraryExport StoryGraph = new StoryGraphExport();

    public static readonly IReadOnlyList<LibraryExport> All = [Hardcover, Goodreads, StoryGraph];

    public abstract string Name { get; }

    /// <summary>What the downloaded file is called, so the reader knows which one to pick.</summary>
    public abstract string FileName { get; }

    /// <summary>How to get the file out of the site, one step a line.</summary>
    public abstract IReadOnlyList<string> Steps { get; }

    /// <summary>The columns only this site's export has together; how a file is recognised.</summary>
    protected abstract IReadOnlyList<string> Needed { get; }

    protected abstract ImportedBook Read(Row row);

    /// <summary>
    /// Reads a file as whichever of the three it turns out to be — a reader who picked Goodreads
    /// and dropped their StoryGraph file meant the file — and says which that was. When it is none
    /// of them, the problem is told in terms of the site the reader picked.
    /// </summary>
    public static (LibraryExport Source, IReadOnlyList<ImportedBook> Books) Parse(string text, LibraryExport picked)
    {
        var rows = Csv.Read(text);
        if (rows.Count == 0)
        {
            throw new NotAnExport("The file is empty.");
        }

        var header = rows[0].Select(cell => cell.Trim()).ToList();
        var source = All.FirstOrDefault(export => export.Needed.All(header.Contains));
        if (source is null)
        {
            var missing = picked.Needed.First(column => !header.Contains(column));
            throw new NotAnExport($"This does not look like a {picked.Name} export: there is no '{missing}' column.");
        }

        var books = rows.Skip(1)
            .Select(cells => new Row(header, cells))
            .Where(row => row["Title"].Length > 0)
            .Select(source.Read)
            .ToList();

        return (source, books);
    }

    protected readonly record struct Row(List<string> Header, List<string> Cells)
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

    /// <summary>
    /// The first of these that is an ISBN. Goodreads writes its ISBNs as spreadsheet formulas —
    /// <c>="9780156030083"</c> — so a cell is stripped of those before it is judged; and
    /// StoryGraph's ISBN column holds its own ids for books that have none, which are not ISBNs.
    /// </summary>
    protected static string? FirstIsbn(params string[] cells) =>
        cells
            .Select(cell => cell.Replace("=", "").Replace("\"", "").Replace("-", "").Replace(" ", ""))
            .FirstOrDefault(cell => IsbnShape().IsMatch(cell));

    protected static int? PagesOf(string cell) => int.TryParse(cell, out var pages) && pages > 0 ? pages : null;

    /// <summary>Hardcover writes 2026-05-15; Goodreads 2026/05/15; either may drop a leading zero.</summary>
    protected static DateOnly? DayOf(string cell) =>
        DateOnly.TryParseExact(cell, DayFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    private static readonly string[] DayFormats = ["yyyy-MM-dd", "yyyy/MM/dd", "yyyy-M-d", "yyyy/M/d"];

    protected static IReadOnlyList<string> SplitAuthors(params string[] cells) =>
        [.. cells.SelectMany(cell => cell.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))];

    [GeneratedRegex(@"^(\d{9}[\dX]|\d{13})$")]
    private static partial Regex IsbnShape();

    private sealed class HardcoverExport : LibraryExport
    {
        public override string Name => "Hardcover";
        public override string FileName => "hardcover-export-….csv";
        public override IReadOnlyList<string> Steps => ["In Hardcover, open Settings.", "Choose Export and download the CSV."];
        protected override IReadOnlyList<string> Needed => ["Title", "Author", "Status"];

        protected override ImportedBook Read(Row row) => new(
            row["Title"],
            SplitAuthors(row["Author"]),
            FirstIsbn(row["ISBN 13"], row["ISBN 10"]),
            PagesOf(row["Pages"]),
            row["Status"].ToLowerInvariant() switch
            {
                "read" => "Finished",
                "currently reading" => "Reading",
                "did not finish" => "Dropped",
                "paused" => "OnHold",
                _ => "WantToRead",
            },
            DayOf(row["Date Finished"]));
    }

    private sealed partial class GoodreadsExport : LibraryExport
    {
        public override string Name => "Goodreads";
        public override string FileName => "goodreads_library_export.csv";
        public override IReadOnlyList<string> Steps =>
        [
            "In Goodreads, open My Books.",
            "Under Tools, choose Import and export.",
            "Choose Export Library, wait for the link, then download it.",
        ];
        protected override IReadOnlyList<string> Needed => ["Title", "Author", "Exclusive Shelf"];

        protected override ImportedBook Read(Row row) => new(
            SeriesMark().Replace(row["Title"], ""),
            SplitAuthors(row["Author"], row["Additional Authors"]),
            FirstIsbn(row["ISBN13"], row["ISBN"]),
            PagesOf(row["Number of Pages"]),
            StatusOf(row["Exclusive Shelf"]),
            DayOf(row["Date Read"]));

        /// <summary>
        /// Goodreads has three shelves of its own; any other is one the reader made, and its name
        /// is all there is to go on. One that says the book was given up or set aside is taken at
        /// its word; the rest are Want to read, which claims nothing.
        /// </summary>
        private static string StatusOf(string shelf) => shelf.ToLowerInvariant() switch
        {
            "read" => "Finished",
            "currently-reading" => "Reading",
            "to-read" => "WantToRead",
            var own when own.Contains("not-finish") || own.Contains("dnf") || own.Contains("abandon") || own.Contains("dropped") => "Dropped",
            var own when own.Contains("hold") || own.Contains("pause") => "OnHold",
            _ => "WantToRead",
        };

        /// <summary>Goodreads puts the series in the title — "The Last Wish (The Witcher, #0.5)" — which a search would trip on.</summary>
        [GeneratedRegex(@"\s*\([^()]*#[\d.]+\)\s*$")]
        private static partial Regex SeriesMark();
    }

    private sealed class StoryGraphExport : LibraryExport
    {
        public override string Name => "StoryGraph";
        public override string FileName => "a .csv file";
        public override IReadOnlyList<string> Steps =>
        [
            "On the StoryGraph website — the app cannot — open your picture, then Manage Account.",
            "Under Manage Your Data, choose Export StoryGraph Library.",
            "Download the CSV when it is ready.",
        ];
        protected override IReadOnlyList<string> Needed => ["Title", "Authors", "Read Status"];

        /// <summary>StoryGraph keeps no page count; the catalogue fills it in when it finds the book.</summary>
        protected override ImportedBook Read(Row row) => new(
            row["Title"],
            SplitAuthors(row["Authors"]),
            FirstIsbn(row["ISBN/UID"]),
            null,
            row["Read Status"].ToLowerInvariant().Replace(' ', '-') switch
            {
                "read" => "Finished",
                "currently-reading" => "Reading",
                "did-not-finish" => "Dropped",
                "paused" => "OnHold",
                _ => "WantToRead",
            },
            DayOf(row["Last Date Read"]));
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
