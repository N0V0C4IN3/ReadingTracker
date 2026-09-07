namespace ReadingTracker.Catalog.Books;

/// <summary>
/// The outcome of a Catalog search. An empty result means the book could not be found;
/// an unavailable result means we could not find out, which the caller must not present
/// to a reader as "this book doesn't exist".
/// </summary>
public sealed record CatalogSearch(SearchStatus Status, IReadOnlyList<Book> Books)
{
    public static CatalogSearch Completed(IReadOnlyList<Book> books) => new(SearchStatus.Completed, books);

    public static CatalogSearch Unavailable { get; } = new(SearchStatus.ProvidersUnavailable, []);
}
