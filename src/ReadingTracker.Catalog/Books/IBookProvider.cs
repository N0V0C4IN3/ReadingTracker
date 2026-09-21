namespace ReadingTracker.Catalog.Books;

/// <summary>
/// An external source of Book metadata.
/// </summary>
public interface IBookProvider
{
    /// <summary>Which provider this is, so a Book can be taken back to the one that described it.</summary>
    BookSource Source { get; }

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

    /// <summary>
    /// The longer details of one volume this provider described earlier, by the id it gave it.
    /// Null when the provider no longer has that volume — an answer, as opposed to the
    /// exception an unreachable provider throws.
    /// </summary>
    Task<BookDetails?> FindDetailsAsync(string externalId, CancellationToken cancellationToken);

    /// <summary>The page a reader can be sent to for this volume, on the provider's own site.</summary>
    Uri PageFor(string externalId);
}
