namespace ReadingTracker.Catalog.Books;

/// <summary>
/// A candidate Book as a provider described it, before Catalog has stored it. Carries no
/// BookId of ours, since it may not correspond to anything cached yet.
/// </summary>
/// <param name="Title">The book's title.</param>
/// <param name="Authors">Author names, in the order the provider listed them.</param>
/// <param name="Isbn">The ISBN, where the provider reported one.</param>
/// <param name="CoverUrl">A link to cover art, where the provider has one.</param>
/// <param name="TotalPages">Page count, where the provider knows it.</param>
/// <param name="Source">Which provider produced this result.</param>
/// <param name="ExternalId">The provider's own identifier for the volume.</param>
public sealed record BookSearchResult(
    string Title,
    IReadOnlyList<string> Authors,
    string? Isbn,
    string? CoverUrl,
    int? TotalPages,
    BookSource Source,
    string? ExternalId);
