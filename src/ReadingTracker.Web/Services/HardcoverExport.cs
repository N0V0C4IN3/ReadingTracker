using System.Globalization;

namespace ReadingTracker.Web.Services;

/// <summary>
/// One book as Hardcover's export describes it, already in this shelf's words: the ISBN that
/// names the edition (the 13 when there is one, else the 10), the ReadingStatus, and the day it
/// was finished when Hardcover knew it.
/// </summary>
public sealed record ImportedBook(
    string Title,
    IReadOnlyList<string> Authors,
    string? Isbn,
    int? Pages,
    string Status,
    DateOnly? FinishedOn);

public sealed class NotAHardcoverExport(string message) : Exception(message);

/// <summary>
/// Reads the CSV Hardcover exports (Settings → Export). A review there is JSON inside a quoted
/// cell, commas and doubled quotes and all, so this is a real CSV reader and not a split on
/// commas. Only the columns the shelf has a place for are kept: title, author, ISBNs, pages,
/// status and the day finished. Ratings, reviews, lists and the rest have no home here.
/// </summary>
public static class HardcoverExport
{
    private static readonly string[] Needed = ["Title", "Author", "Status"];

    public static IReadOnlyList<ImportedBook> Parse(string text)
    {
        var rows = Csv.Read(text);
        if (rows.Count == 0)
        {
            throw new NotAHardcoverExport("The file is empty.");
        }

        var header = rows[0].Select(cell => cell.Trim()).ToList();
        foreach (var column in Needed)
        {
            if (!header.Contains(column))
            {
                throw new NotAHardcoverExport($"This does not look like a Hardcover export: there is no '{column}' column.");
            }
        }

        var books = new List<ImportedBook>();
        foreach (var row in rows.Skip(1))
        {
            string Cell(string column)
            {
                var at = header.IndexOf(column);
                return at >= 0 && at < row.Count ? row[at].Trim() : "";
            }

            var title = Cell("Title");
            if (title.Length == 0)
            {
                continue;
            }

            books.Add(new ImportedBook(
                title,
                SplitAuthors(Cell("Author")),
                FirstIsbn(Cell("ISBN 13"), Cell("ISBN 10")),
                int.TryParse(Cell("Pages"), out var pages) && pages > 0 ? pages : null,
                StatusOf(Cell("Status")),
                DateOnly.TryParseExact(Cell("Date Finished"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var finished) ? finished : null));
        }

        return books;
    }

    /// <summary>
    /// Hardcover's shelves in this one's words. Anything it adds later lands on Want to Read,
    /// which is where a book whose state is unknown belongs — nothing is claimed about it.
    /// </summary>
    private static string StatusOf(string hardcover) => hardcover.Trim().ToLowerInvariant() switch
    {
        "read" => "Finished",
        "currently reading" => "Reading",
        "did not finish" => "Dropped",
        "paused" => "OnHold",
        _ => "WantToRead",
    };

    private static string? FirstIsbn(string thirteen, string ten)
    {
        var chosen = thirteen.Length > 0 ? thirteen : ten;
        return chosen.Length > 0 ? chosen.Replace("-", "").Replace(" ", "") : null;
    }

    private static IReadOnlyList<string> SplitAuthors(string authors) =>
        [.. authors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>RFC 4180, which is what Hardcover writes: quoted cells, doubled quotes, newlines inside quotes.</summary>
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
