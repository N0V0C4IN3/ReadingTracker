namespace ReadingTracker.Catalog.Books;

/// <summary>
/// The outcome of a Catalog search. An empty result means the book could not be found;
/// an unavailable result means we could not find out, which the caller must not present
/// to a reader as "this book doesn't exist". <paramref name="HasMore"/> says whether there
/// is a page beyond the one returned.
/// </summary>
public sealed record CatalogSearch(SearchStatus Status, IReadOnlyList<Book> Books, bool HasMore)
{
    public static CatalogSearch Completed(IReadOnlyList<Book> books, bool hasMore = false) =>
        new(SearchStatus.Completed, books, hasMore);

    public static CatalogSearch Unavailable { get; } = new(SearchStatus.ProvidersUnavailable, [], false);
}
