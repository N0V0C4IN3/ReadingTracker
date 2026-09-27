namespace ReadingTracker.Web.Services;

/// <summary>How a book is named in words, the same wherever it is shown in full.</summary>
public static class BookText
{
    /// <summary>The authors, and after them who put the book out and when, the way a jacket flap says it.</summary>
    public static string? Byline(IReadOnlyList<string>? authors, string? imprint)
    {
        var names = authors is { Count: > 0 } ? string.Join(", ", authors) : null;
        var byline = string.Join(" · ", new[] { names, imprint }.OfType<string>());

        return byline.Length == 0 ? null : byline;
    }

    /// <summary>The letter a cover with no picture shows.</summary>
    public static string Initial(string? title) =>
        (title ?? "?").Trim() is { Length: > 0 } trimmed ? trimmed[..1].ToUpperInvariant() : "?";
}
