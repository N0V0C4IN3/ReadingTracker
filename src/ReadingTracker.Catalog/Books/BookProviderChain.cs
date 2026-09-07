using System.Text.Json;

namespace ReadingTracker.Catalog.Books;

public enum SearchStatus
{
    /// <summary>At least one provider answered. There may or may not have been matches.</summary>
    Completed,

    /// <summary>No provider could be reached, so we genuinely don't know whether the book exists.</summary>
    ProvidersUnavailable,
}

public sealed record ProviderSearch(SearchStatus Status, IReadOnlyList<BookSearchResult> Results)
{
    public static ProviderSearch Completed(IReadOnlyList<BookSearchResult> results) => new(SearchStatus.Completed, results);

    public static ProviderSearch Unavailable { get; } = new(SearchStatus.ProvidersUnavailable, []);
}

/// <summary>
/// Asks each provider in turn and takes the first real answer, so a gap or an outage at one
/// provider doesn't make a book unfindable. Distinguishes "nobody has this book" from
/// "nobody could be reached", which are very different answers to give a reader.
/// </summary>
public sealed class BookProviderChain(IEnumerable<IBookProvider> providers, ILogger<BookProviderChain> logger)
{
    public Task<ProviderSearch> SearchByIsbnAsync(string isbn, CancellationToken cancellationToken) =>
        AskEachAsync(
            provider => provider.SearchByIsbnAsync(isbn, cancellationToken),
            isbn,
            cancellationToken);

    public Task<ProviderSearch> SearchByTitleAndAuthorAsync(
        string? title,
        string? author,
        CancellationToken cancellationToken) =>
        AskEachAsync(
            provider => provider.SearchByTitleAndAuthorAsync(title, author, cancellationToken),
            $"{title} / {author}",
            cancellationToken);

    private async Task<ProviderSearch> AskEachAsync(
        Func<IBookProvider, Task<IReadOnlyList<BookSearchResult>>> ask,
        string searchedFor,
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
                    return ProviderSearch.Completed(results);
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
