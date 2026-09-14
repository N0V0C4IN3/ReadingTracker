namespace ReadingTracker.Catalog.Books;

/// <summary>
/// An external source of Book metadata.
/// </summary>
public interface IBookProvider
{
    Task<IReadOnlyList<BookSearchResult>> SearchByIsbnAsync(string isbn, CancellationToken cancellationToken);

    /// <summary>
    /// Finds candidates by title, narrowed by author when one is given. Unlike an ISBN
    /// lookup this is fuzzy, so several results are normal and the caller picks — enough of
    /// them that the caller asks for one <paramref name="window"/> of answers at a time.
    /// </summary>
    Task<IReadOnlyList<BookSearchResult>> SearchByTitleAndAuthorAsync(
        string? title,
        string? author,
        SearchWindow window,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds candidates for words the reader typed without saying what they were — a title, an
    /// author, some of each. Each provider has a search of its own that reads every field, and
    /// this hands the words to it as they are.
    /// </summary>
    Task<IReadOnlyList<BookSearchResult>> SearchAsync(
        string query,
        SearchWindow window,
        CancellationToken cancellationToken);
}
