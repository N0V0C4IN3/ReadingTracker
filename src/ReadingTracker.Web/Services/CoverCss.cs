namespace ReadingTracker.Web.Services;

/// <summary>
/// A book's cover handed to the stylesheet, for what it draws from the cover itself: the band
/// across a phone's shelf card, the edge of a book in the sideways cover flow.
/// </summary>
public static class CoverCss
{
    /// <summary>
    /// The cover's address as a CSS url(), quoted, with anything that could end the string or the
    /// declaration percent-encoded. It goes into a style attribute, and a cover's address comes
    /// from whoever added the book.
    /// </summary>
    public static string Url(string address) =>
        $"url(\"{address
            .Replace("\\", "%5C")
            .Replace("\"", "%22")
            .Replace("\n", "%0A")
            .Replace("\r", "%0D")}\")";
}
