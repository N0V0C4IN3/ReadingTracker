namespace ReadingTracker.Catalog.Books;

/// <summary>Where a Book's metadata came from.</summary>
public enum BookSource
{
    GoogleBooks,
    OpenLibrary,
    Manual,
}

/// <summary>
/// Catalog metadata for a title, as stored. Shared across all users: a Book is not owned by
/// anyone, and carries no reading state. Cached the first time it is referenced rather than
/// re-fetched from a provider on every use.
/// </summary>
public sealed class Book
{
    public Guid Id { get; init; }

    public required string Title { get; init; }

    public required List<string> Authors { get; init; }

    public string? Isbn { get; init; }

    public string? CoverUrl { get; init; }

    public int? TotalPages { get; init; }

    public BookSource Source { get; init; }

    /// <summary>The provider's own identifier, used to tell editions apart when there is no ISBN.</summary>
    public string? ExternalId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
