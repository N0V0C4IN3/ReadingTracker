namespace ReadingTracker.Catalog.Books;

/// <summary>
/// Announces that Catalog has stored a Book it had never seen before. Published once, when
/// the Book first appears — not when an already-known Book is referenced again.
/// </summary>
public sealed record BookCached(
    Guid BookId,
    string Title,
    string? Isbn,
    string Source,
    DateTimeOffset CachedAt);

public interface IBookEvents
{
    Task PublishAsync(BookCached bookCached, CancellationToken cancellationToken);
}
