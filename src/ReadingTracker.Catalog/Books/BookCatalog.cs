using Microsoft.EntityFrameworkCore;
using ReadingTracker.Catalog.Persistence;

namespace ReadingTracker.Catalog.Books;

/// <summary>
/// Catalog's view of what books exist. Answers searches from what has already been cached,
/// and falls back to an external provider only for titles it has never seen — persisting
/// them on first reference so every later lookup has a stable BookId to point at.
/// </summary>
public sealed class BookCatalog(CatalogDbContext database, BookProviderChain providers, TimeProvider clock)
{
    public async Task<CatalogSearch> SearchByIsbnAsync(string isbn, CancellationToken cancellationToken)
    {
        var alreadyCached = await database.Books
            .Where(book => book.Isbn == isbn)
            .ToListAsync(cancellationToken);

        if (alreadyCached.Count > 0)
        {
            // Already known, so a provider outage is irrelevant here.
            return CatalogSearch.Completed(alreadyCached);
        }

        var search = await providers.SearchByIsbnAsync(isbn, cancellationToken);

        if (search.Status is SearchStatus.ProvidersUnavailable)
        {
            return CatalogSearch.Unavailable;
        }

        var found = search.Results;

        if (found.Count == 0)
        {
            return CatalogSearch.Completed([]);
        }

        // A provider can return the same ISBN more than once in one response; those are the
        // same book to us, so only the first becomes a Book.
        var books = found
            .GroupBy(result => result.Isbn ?? isbn)
            .Select(duplicates => ToBook(duplicates.First()))
            .ToList();

        database.Books.AddRange(books);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another request cached this ISBN between our lookup and our insert. Theirs is
            // just as good, and the unique index means only one of us can win.
            foreach (var entry in database.ChangeTracker.Entries<Book>().ToList())
            {
                entry.State = EntityState.Detached;
            }

            return CatalogSearch.Completed(await database.Books
                .Where(book => book.Isbn == isbn)
                .ToListAsync(cancellationToken));
        }

        return CatalogSearch.Completed(books);
    }

    /// <summary>
    /// Records a Book nobody could find, for the times a reader owns something no provider
    /// has heard of. Returns null when its ISBN is already taken, since there is one Book
    /// per ISBN and the caller should be told rather than silently given a duplicate.
    /// </summary>
    public async Task<Book?> AddByHandAsync(
        string title,
        IReadOnlyList<string> authors,
        string? isbn,
        string? coverUrl,
        int? totalPages,
        CancellationToken cancellationToken)
    {
        var book = new Book
        {
            Id = Guid.CreateVersion7(),
            Title = title.Trim(),
            Authors = [.. authors.Select(author => author.Trim())],
            Isbn = isbn,
            CoverUrl = coverUrl,
            TotalPages = totalPages,
            Source = BookSource.Manual,
            ExternalId = null,
            CreatedAt = clock.GetUtcNow(),
        };

        database.Books.Add(book);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            database.Entry(book).State = EntityState.Detached;
            return null;
        }

        return book;
    }

    public Task<Book?> FindAsync(Guid bookId, CancellationToken cancellationToken) =>
        database.Books.FirstOrDefaultAsync(book => book.Id == bookId, cancellationToken);

    private Book ToBook(BookSearchResult result) => new()
    {
        Id = Guid.CreateVersion7(),
        Title = result.Title,
        Authors = [.. result.Authors],
        Isbn = result.Isbn,
        CoverUrl = result.CoverUrl,
        TotalPages = result.TotalPages,
        Source = result.Source,
        ExternalId = result.ExternalId,
        CreatedAt = clock.GetUtcNow(),
    };
}
