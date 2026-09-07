namespace ReadingTracker.Catalog.Books;

/// <summary>
/// An external source of Book metadata.
/// </summary>
public interface IBookProvider
{
    Task<IReadOnlyList<BookSearchResult>> SearchByIsbnAsync(string isbn, CancellationToken cancellationToken);
}
