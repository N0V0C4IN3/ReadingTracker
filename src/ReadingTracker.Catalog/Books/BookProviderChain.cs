using System.Text.Json;

namespace ReadingTracker.Catalog.Books;

public enum SearchStatus
{
    /// <summary>At least one provider answered. There may or may not have been matches.</summary>
    Completed,

    /// <summary>No provider could be reached, so we genuinely don't know whether the book exists.</summary>
    ProvidersUnavailable,
}

/// <summary>
/// What a provider said. <paramref name="HasMore"/> means the provider filled the window it was
/// given, so there is very likely another page behind this one — it is answered from what the
/// provider returned rather than from what was eventually stored, because de-duplication can
/// shrink a full page and must not be mistaken for the end of the results.
/// </summary>
public sealed record ProviderSearch(SearchStatus Status, IReadOnlyList<BookSearchResult> Results, bool HasMore)
{
    public static ProviderSearch Completed(IReadOnlyList<BookSearchResult> results, bool hasMore = false) =>
        new(SearchStatus.Completed, results, hasMore);

    public static ProviderSearch Unavailable { get; } = new(SearchStatus.ProvidersUnavailable, [], false);
}

/// <summary>
/// Asks each provider in turn and takes the first real answer, so a gap or an outage at one
/// provider doesn't make a book unfindable. Distinguishes "nobody has this book" from
/// "nobody could be reached", which are very different answers to give a reader.
/// </summary>
public sealed class BookProviderChain(IEnumerable<IBookProvider> providers, ILogger<BookProviderChain> logger)
{
    /// <summary>An ISBN names one edition, so there is never a second page of it to ask for.</summary>
    public Task<ProviderSearch> SearchByIsbnAsync(string isbn, CancellationToken cancellationToken) =>
        AskEachAsync(
            provider => provider.SearchByIsbnAsync(isbn, cancellationToken),
            isbn,
            window: null,
            cancellationToken);

    public Task<ProviderSearch> SearchByTitleAndAuthorAsync(
        string? title,
        string? author,
        SearchWindow window,
        CancellationToken cancellationToken) =>
        AskEachAsync(
            provider => provider.SearchByTitleAndAuthorAsync(title, author, window, cancellationToken),
            $"{title} / {author}",
            window,
            cancellationToken);

    /// <summary>
    /// Each provider is asked for the same window, and the first with anything to say answers.
    /// A search whose later page falls through to the next provider gets that provider's window
    /// of the same query — a real answer, if not a continuation of the one before it.
    /// </summary>
    private async Task<ProviderSearch> AskEachAsync(
        Func<IBookProvider, Task<IReadOnlyList<BookSearchResult>>> ask,
        string searchedFor,
        SearchWindow? window,
        CancellationToken cancellationToken)
    {
        var someProviderAnswered = false;

        foreach (var provider in providers)
        {
            try
            {
                var results = await ask(provider);
                someProviderAnswered = true;

                if (results.Count > 0)
                {
                    return ProviderSearch.Completed(results, window is { } asked && results.Count >= asked.PageSize);
                }
            }
            catch (Exception exception) when (IsProviderFailure(exception, cancellationToken))
            {
                logger.LogWarning(
                    exception,
                    "Book provider {Provider} could not be reached while searching for {SearchedFor}",
                    provider.GetType().Name,
                    searchedFor);
            }
        }

        return someProviderAnswered ? ProviderSearch.Completed([]) : ProviderSearch.Unavailable;
    }

    /// <summary>
    /// A provider being unreachable is expected and recoverable. The caller giving up is not:
    /// that cancellation belongs to the request and must not be mistaken for an outage.
    /// </summary>
    private static bool IsProviderFailure(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested
        && exception is HttpRequestException or JsonException or TaskCanceledException or TimeoutException;
}
