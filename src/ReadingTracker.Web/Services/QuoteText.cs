namespace ReadingTracker.Web.Services;

/// <summary>A Quote as text to take somewhere else.</summary>
public static class QuoteText
{
    /// <summary>
    /// What Copy puts on the clipboard: the passage in typographer's quotes, then whose words and
    /// which book, the authors named the way the byline names them. The reader's note stays
    /// behind — what is shared is the Book's words, and the note is theirs.
    /// </summary>
    public static string ForCopy(string passage, IReadOnlyList<string>? authors, string? title)
    {
        var source = string.Join(", ", new[] { BookText.Byline(authors, null), title }.OfType<string>());
        var quoted = $"“{passage.Trim()}”";

        return source.Length == 0 ? quoted : $"{quoted} — {source}";
    }
}
