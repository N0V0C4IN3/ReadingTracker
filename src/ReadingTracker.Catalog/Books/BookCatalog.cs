using Microsoft.EntityFrameworkCore;
using ReadingTracker.Catalog.Persistence;

namespace ReadingTracker.Catalog.Books;

/// <summary>
/// Catalog's view of what books exist. Answers searches from what has already been cached
/// where it can, and falls back to external providers otherwise — persisting what they
/// return so every later lookup has a stable BookId to point at.
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

        return search.Status is SearchStatus.ProvidersUnavailable
            ? CatalogSearch.Unavailable
            : CatalogSearch.Completed(await StoreAsync(search.Results, cancellationToken));
    }

    /// <summary>
    /// Unlike an ISBN, a title/author query is fuzzy: there is no reliable way to tell that
    /// the cache already holds everything it would match, so providers are always asked.
    /// What they return is still cached.
    /// </summary>
    public async Task<CatalogSearch> SearchByTitleAndAuthorAsync(
        string? title,
        string? author,
        CancellationToken cancellationToken)
    {
        var search = await providers.SearchByTitleAndAuthorAsync(title, author, cancellationToken);

        return search.Status is SearchStatus.ProvidersUnavailable
            ? CatalogSearch.Unavailable
            : CatalogSearch.Completed(await StoreAsync(search.Results, cancellationToken));
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

    /// <summary>
    /// Turns provider results into stored Books, reusing any that are already known so the
    /// same title never ends up with two BookIds.
    /// </summary>
    private async Task<IReadOnlyList<Book>> StoreAsync(
        IReadOnlyList<BookSearchResult> results,
        CancellationToken cancellationToken)
    {
        if (results.Count == 0)
        {
            return [];
        }

        // A provider can list the same book twice in one response; those are one book to us.
        var distinct = results
            .Select((result, position) => (result, position))
            .GroupBy(entry => IdentityOf(entry.result, entry.position))
            .Select(duplicates => duplicates.First().result)
            .ToList();

        var isbns = distinct.Where(result => result.Isbn is not null).Select(result => result.Isbn!).ToList();
        var externalIds = distinct.Where(result => result.ExternalId is not null).Select(result => result.ExternalId!).ToList();

        var known = await FindKnownAsync(isbns, externalIds, cancellationToken);

        var books = new List<Book>(distinct.Count);
        var toInsert = new List<Book>();

        foreach (var result in distinct)
        {
            var alreadyKnown = known.FirstOrDefault(book => IsSameBook(book, result));

            if (alreadyKnown is not null)
            {
                books.Add(alreadyKnown);
                continue;
            }

            var book = ToBook(result);
            toInsert.Add(book);
            books.Add(book);
        }

        if (toInsert.Count == 0)
        {
            return books;
        }

        database.Books.AddRange(toInsert);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another request stored one of these between our lookup and our insert. Theirs
            // is just as good, and the unique index means only one of us can win.
            foreach (var entry in database.ChangeTracker.Entries<Book>().ToList())
            {
                entry.State = EntityState.Detached;
            }

            return await FindKnownAsync(isbns, externalIds, cancellationToken);
        }

        return books;
    }

    private Task<List<Book>> FindKnownAsync(
        List<string> isbns,
        List<string> externalIds,
        CancellationToken cancellationToken) =>
        database.Books
            .Where(book =>
                (book.Isbn != null && isbns.Contains(book.Isbn))
                || (book.ExternalId != null && externalIds.Contains(book.ExternalId)))
            .ToListAsync(cancellationToken);

    /// <summary>ISBN identifies a book where there is one; otherwise the provider's own id does.</summary>
    private static string IdentityOf(BookSearchResult result, int position) =>
        result.Isbn
        ?? (result.ExternalId is null ? $"unidentified:{position}" : $"{result.Source}:{result.ExternalId}");

    private static bool IsSameBook(Book book, BookSearchResult result) =>
        result.Isbn is not null
            ? book.Isbn == result.Isbn
            : result.ExternalId is not null
              && book.Source == result.Source
              && book.ExternalId == result.ExternalId;

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
