namespace ReadingTracker.Catalog.Books;

/// <summary>
/// A candidate Book returned by a search, as the provider described it. Not yet persisted,
/// so it carries no BookId of ours.
/// </summary>
/// <param name="Title">The book's title.</param>
/// <param name="Authors">Author names, in the order the provider listed them.</param>
/// <param name="Isbn">The ISBN, where the provider reported one.</param>
/// <param name="CoverUrl">A link to cover art, where the provider has one.</param>
/// <param name="TotalPages">Page count, where the provider knows it.</param>
public sealed record BookSearchResult(
    string Title,
    IReadOnlyList<string> Authors,
    string? Isbn,
    string? CoverUrl,
    int? TotalPages);
