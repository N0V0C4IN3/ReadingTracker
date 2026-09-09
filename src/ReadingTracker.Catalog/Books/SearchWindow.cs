namespace ReadingTracker.Catalog.Books;

/// <summary>
/// Which slice of a fuzzy search's answers is being asked for. Pages are 1-based because that
/// is what a reader is shown; providers are told the offset they want instead.
/// </summary>
public sealed record SearchWindow(int Page, int PageSize)
{
    /// <summary>Matches what both providers returned before anyone asked for a second page.</summary>
    public const int DefaultPageSize = 10;

    /// <summary>Google Books will not return more than this, so it is the ceiling for everyone.</summary>
    public const int MaxPageSize = 40;

    public static SearchWindow First { get; } = new(1, DefaultPageSize);

    public int Offset => (Page - 1) * PageSize;
}
