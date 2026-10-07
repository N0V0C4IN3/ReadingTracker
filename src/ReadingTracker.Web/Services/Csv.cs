using System.Text;

namespace ReadingTracker.Web.Services;

/// <summary>
/// RFC 4180, which is what Hardcover, Goodreads and StoryGraph all write: quoted cells, doubled
/// quotes, newlines inside quotes. Reading and writing are kept together so what this app writes
/// is what it reads.
/// </summary>
public static class Csv
{
    public static List<List<string>> Read(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
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

    /// <summary>
    /// Rows as Goodreads writes them: a cell holding a comma, a quote or a line break is quoted,
    /// its quotes doubled, and every line ends in CRLF.
    /// </summary>
    public static string Write(IEnumerable<IEnumerable<string>> rows)
    {
        var text = new StringBuilder();

        foreach (var row in rows)
        {
            text.Append(string.Join(',', row.Select(Quoted))).Append("\r\n");
        }

        return text.ToString();
    }

    private static string Quoted(string cell) =>
        cell.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? $"\"{cell.Replace("\"", "\"\"")}\"" : cell;
}
