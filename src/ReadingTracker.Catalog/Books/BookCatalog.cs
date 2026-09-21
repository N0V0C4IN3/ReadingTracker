using Microsoft.EntityFrameworkCore;
using ReadingTracker.Catalog.Persistence;

namespace ReadingTracker.Catalog.Books;

/// <summary>
/// Catalog's view of what books exist. Answers searches from what has already been cached
/// where it can, and falls back to external providers otherwise — persisting what they
/// return so every later lookup has a stable BookId to point at.
/// </summary>
public sealed class BookCatalog(
    CatalogDbContext database,
    BookProviderChain providers,
    IBookEvents events,
    TimeProvider clock)
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
    /// the cache already holds everything it would match, so providers are always asked for
    /// the <paramref name="window"/> wanted. What they return is still cached.
    /// </summary>
    public async Task<CatalogSearch> SearchByTitleAndAuthorAsync(
        string? title,
        string? author,
        SearchWindow window,
        CancellationToken cancellationToken)
    {
        var search = await providers.SearchByTitleAndAuthorAsync(title, author, window, cancellationToken);

        return search.Status is SearchStatus.ProvidersUnavailable
            ? CatalogSearch.Unavailable
            : CatalogSearch.Completed(await StoreAsync(search.Results, cancellationToken), search.HasMore);
    }

    /// <summary>
    /// Words with no field named — as fuzzy as a title/author query, and treated the same way:
    /// providers are always asked, and what they return is cached.
    /// </summary>
    public async Task<CatalogSearch> SearchAsync(
        string query,
        SearchWindow window,
        CancellationToken cancellationToken)
    {
        var search = await providers.SearchAsync(query, window, cancellationToken);

        return search.Status is SearchStatus.ProvidersUnavailable
            ? CatalogSearch.Unavailable
            : CatalogSearch.Completed(await StoreAsync(search.Results, cancellationToken), search.HasMore);
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

        await AnnounceAsync([book], cancellationToken);

        return book;
    }

    /// <summary>
    /// Looks up many Books at once. Ids matching nothing are left out rather than reported,
    /// so one stale id doesn't spoil a whole page of results.
    /// </summary>
    public async Task<IReadOnlyList<Book>> FindAllAsync(
        IReadOnlyList<Guid> bookIds,
        CancellationToken cancellationToken) =>
        await database.Books
            .Where(book => bookIds.Contains(book.Id))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// One Book in full. A Book that came from a provider without its longer details is asked
    /// about here, the first time — by the id the provider gave it — and what came back is
    /// kept, even when it is nothing, so no Book is asked about twice. A provider that cannot
    /// be reached is the one case left unrecorded: the Book is answered as it is, and the next
    /// lookup tries again. A Book entered by hand has no provider to ask.
    /// </summary>
    public async Task<BookLookup?> FindAsync(Guid bookId, CancellationToken cancellationToken)
    {
        var book = await database.Books.FirstOrDefaultAsync(book => book.Id == bookId, cancellationToken);

        if (book is null)
        {
            return null;
        }

        if (book.DetailsLookedAt is not null || book.Source == BookSource.Manual || book.ExternalId is null)
        {
            return new BookLookup(book, PageFor(book), DetailsUnavailable: false);
        }

        var answer = await providers.FindDetailsAsync(book.Source, book.ExternalId, cancellationToken);

        if (answer.Unreachable)
        {
            return new BookLookup(book, PageFor(book), DetailsUnavailable: true);
        }

        book.Fill(answer.Details ?? BookDetails.None, clock.GetUtcNow());
        await database.SaveChangesAsync(cancellationToken);

        return new BookLookup(book, PageFor(book), DetailsUnavailable: false);
    }

    private Uri? PageFor(Book book) =>
        book.ExternalId is null ? null : providers.PageFor(book.Source, book.ExternalId);

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

        // Only the newly stored ones: re-referencing a Book we already had is not news.
        await AnnounceAsync(toInsert, cancellationToken);

        return books;
    }

    private async Task AnnounceAsync(IReadOnlyList<Book> newlyStored, CancellationToken cancellationToken)
    {
        foreach (var book in newlyStored)
        {
            await events.PublishAsync(
                new BookCached(book.Id, book.Title, book.Isbn, book.Source.ToString(), book.CreatedAt),
                cancellationToken);
        }
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

    private Book ToBook(BookSearchResult result)
    {
        var now = clock.GetUtcNow();

        var book = new Book
        {
            Id = Guid.CreateVersion7(),
            Title = result.Title,
            Authors = [.. result.Authors],
            Isbn = result.Isbn,
            CoverUrl = result.CoverUrl,
            TotalPages = result.TotalPages,
            Source = result.Source,
            ExternalId = result.ExternalId,
            CreatedAt = now,
        };

        // A search that carried the details has answered the question a later lookup would ask.
        if (result.Details is { } details)
        {
            book.Fill(details, now);
        }

        return book;
    }
}

/// <summary>
/// A Book as answered by <see cref="BookCatalog.FindAsync"/>: the Book, where its provider
/// shows it, and whether its details are missing only because the provider could not be
/// reached just now — as opposed to the provider having none, or the Book having no provider.
/// </summary>
public sealed record BookLookup(Book Book, Uri? ProviderUrl, bool DetailsUnavailable);
