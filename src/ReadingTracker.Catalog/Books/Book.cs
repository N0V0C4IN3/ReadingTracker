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

    // The longer details, which can arrive later than the rest: settable, where everything
    // above is fixed the moment the Book is cached.

    /// <summary>Plain text, paragraphs separated by a blank line.</summary>
    public string? Description { get; set; }

    public string? Publisher { get; set; }

    /// <summary>As the provider gave it — a year, a year and month, or a full date.</summary>
    public string? PublishedDate { get; set; }

    public List<string> Categories { get; set; } = [];

    /// <summary>
    /// When the details were last looked for at the Book's provider; null when they never
    /// were. Recorded whatever the provider had, so a Book it has no description for is asked
    /// about once and not on every visit — and left null when the provider could not be
    /// reached, so the next visit tries again.
    /// </summary>
    public DateTimeOffset? DetailsLookedAt { get; set; }

    public BookDetails Details => new(Description, Publisher, PublishedDate, Categories);

    public void Fill(BookDetails details, DateTimeOffset lookedAt)
    {
        Description = details.Description;
        Publisher = details.Publisher;
        PublishedDate = details.PublishedDate;
        Categories = [.. details.Categories];
        DetailsLookedAt = lookedAt;
    }
}
