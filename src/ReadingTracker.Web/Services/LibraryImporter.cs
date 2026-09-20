namespace ReadingTracker.Web.Services;

public enum ImportOutcome
{
    /// <summary>On the shelf now, at its status, finished on its day.</summary>
    Added,

    /// <summary>Was there already; nothing about it was touched.</summary>
    AlreadyOnShelf,

    /// <summary>Could not be put on the shelf. <see cref="ImportResult.Note"/> says why.</summary>
    Failed,
}

public sealed record ImportResult(ImportedBook Book, ImportOutcome Outcome, string? Note);

/// <summary>
/// Puts one imported book on the shelf through the same doors a reader uses by hand: found in
/// the Catalog by its ISBN, or added there when nobody has heard of it; added to the library;
/// moved to its status with the day it was finished; given Hardcover's page count when Catalog
/// has none. No door is opened specially for imports, so nothing an import does could not
/// have been done by a reader, one book at a time.
///
/// The Gateway paces searches and books-by-hand per reader (ADR-0013), and an export is dozens
/// of each. Rather than ask for a wider door, the import waits: a 429 is a pause for as long as
/// the Gateway said and a retry, and the job says so while it does. A fifty-book shelf takes a
/// few minutes; it is done once.
/// </summary>
public sealed class LibraryImporter(
    GatewayCatalogClient catalog,
    GatewayLibraryClient library,
    Func<TimeSpan, CancellationToken, Task> wait)
{
    /// <summary>When the Gateway does not say how long: its windows are a minute (ADR-0013).</summary>
    private static readonly TimeSpan Pause = TimeSpan.FromSeconds(61);
    private const int MostRetries = 3;

    public async Task<ImportResult> ImportAsync(ImportedBook book, CancellationToken cancellationToken)
    {
        var (found, problem) = await FindOrCreateAsync(book, cancellationToken);
        if (found is null)
        {
            return new(book, ImportOutcome.Failed, problem ?? "could not be found or added to the library");
        }

        var (entry, adding) = await library.AddAsync(found.Id, cancellationToken);
        if (adding == AddToLibraryProblem.AlreadyInLibrary)
        {
            return new(book, ImportOutcome.AlreadyOnShelf, null);
        }

        if (entry is null)
        {
            return new(book, ImportOutcome.Failed, adding switch
            {
                AddToLibraryProblem.NotSignedIn => "your session ended",
                _ => "ReadingTracker could not be reached",
            });
        }

        var notes = new List<string>();

        if (book.Status != "WantToRead")
        {
            var moved = await library.SetStatusAsync(entry.Id, book.Status, cancellationToken, book.FinishedOn);
            if (!moved.Ok)
            {
                notes.Add("added, but its status could not be set");
            }
        }

        if (found.TotalPages is null && book.Pages is { } pages)
        {
            var counted = await library.SetPageCountAsync(entry.Id, pages, cancellationToken);
            if (!counted.Ok)
            {
                notes.Add("its page count could not be set");
            }
        }

        return new(book, ImportOutcome.Added, notes.Count > 0 ? string.Join("; ", notes) : null);
    }

    private async Task<(BookSearchResult? Book, string? Problem)> FindOrCreateAsync(ImportedBook book, CancellationToken cancellationToken)
    {
        if (book.Isbn is { } isbn)
        {
            for (var attempt = 0; ; attempt++)
            {
                var (page, problem) = await catalog.SearchAsync(isbn, 1, cancellationToken);

                if (problem == SearchUnavailable.TooManyRequests && attempt < MostRetries)
                {
                    await PauseAsync(cancellationToken);
                    continue;
                }

                if (problem is { } refused)
                {
                    return (null, refused switch
                    {
                        SearchUnavailable.NotSignedIn => "your session ended",
                        SearchUnavailable.ProvidersUnavailable => "no book provider could be reached",
                        SearchUnavailable.TooManyRequests => "the library did not answer in time",
                        _ => "ReadingTracker could not be reached",
                    });
                }

                if (page is { Results.Count: > 0 })
                {
                    return (page.Results[0], null);
                }

                break;
            }
        }

        // Nobody's provider has it, or there was no ISBN to ask with: it goes in by hand, with
        // what Hardcover knew about it.
        for (var attempt = 0; ; attempt++)
        {
            var created = await catalog.CreateAsync(
                new NewBook(book.Title, book.Authors, book.Isbn, null, book.Pages),
                cancellationToken);

            if (created.Problem == BookCreationProblem.TooManyRequests && attempt < MostRetries)
            {
                await PauseAsync(cancellationToken);
                continue;
            }

            return created.Ok
                ? (created.Book, null)
                : (null, created.Problem switch
                {
                    BookCreationProblem.NotSignedIn => "your session ended",
                    BookCreationProblem.Rejected => "the library would not take it: " + string.Join(" ", created.FieldErrors.Values.SelectMany(v => v)),
                    BookCreationProblem.IsbnAlreadyInCatalog => "the library has its ISBN but could not find it",
                    BookCreationProblem.TooManyRequests => "the library did not answer in time",
                    _ => "ReadingTracker could not be reached",
                });
        }
    }

    /// <summary>As long as the Gateway asked, and a second more so the window has turned.</summary>
    private Task PauseAsync(CancellationToken cancellationToken) =>
        wait((catalog.RetryAfter ?? Pause) + TimeSpan.FromSeconds(1), cancellationToken);
}
