namespace ReadingTracker.Catalog.Books;

public sealed class GoogleBooksOptions
{
    public const string SectionName = "GoogleBooks";

    /// <summary>
    /// Optional. Google Books serves public volume searches without a key; supplying one
    /// raises the quota. Keep it in user-secrets or environment configuration, never in appsettings.
    /// </summary>
    public string? ApiKey { get; set; }

    public Uri BaseAddress { get; set; } = new("https://www.googleapis.com/books/v1/");
}
