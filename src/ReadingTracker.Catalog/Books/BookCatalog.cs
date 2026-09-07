using Microsoft.EntityFrameworkCore;
using ReadingTracker.Catalog.Persistence;

namespace ReadingTracker.Catalog.Books;

/// <summary>
/// Catalog's view of what books exist. Answers searches from what has already been cached,
/// and falls back to an external provider only for titles it has never seen — persisting
/// them on first reference so every later lookup has a stable BookId to point at.
/// </summary>
public sealed class BookCatalog(CatalogDbContext database, IBookProvider provider, TimeProvider clock)
{
    public async Task<IReadOnlyList<Book>> SearchByIsbnAsync(string isbn, CancellationToken cancellationToken)
    {
        var alreadyCached = await database.Books
            .Where(book => book.Isbn == isbn)
            .ToListAsync(cancellationToken);

        if (alreadyCached.Count > 0)
        {
            return alreadyCached;
        }

        var found = await provider.SearchByIsbnAsync(isbn, cancellationToken);

        if (found.Count == 0)
        {
            return [];
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

            return await database.Books
                .Where(book => book.Isbn == isbn)
                .ToListAsync(cancellationToken);
        }

        return books;
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
