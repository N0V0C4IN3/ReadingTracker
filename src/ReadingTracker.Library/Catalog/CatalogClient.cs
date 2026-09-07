using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Library.Catalog;

/// <summary>
/// A Book as Catalog describes it. Library holds no copy of this: it is fetched when needed,
/// so it can never drift from what Catalog says.
/// </summary>
public sealed record CatalogBook(
    Guid Id,
    string Title,
    IReadOnlyList<string> Authors,
    string? Isbn,
    string? CoverUrl,
    int? TotalPages);

/// <summary>
/// Library's window onto Catalog. Per ADR-0004 this goes over HTTP; Library never reads
/// Catalog's schema.
/// </summary>
public sealed class CatalogClient(HttpClient httpClient, ILogger<CatalogClient> logger)
{
    /// <summary>
    /// Returns null when Catalog has no such Book. Throws when Catalog cannot be reached,
    /// because a caller checking whether a Book exists must not read "unreachable" as "no".
    /// </summary>
    public async Task<CatalogBook?> FindBookAsync(Guid bookId, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync($"api/books/{bookId}", cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<CatalogBook>(cancellationToken);
    }

    /// <summary>
    /// Looks up many Books in one request. Best-effort by design: if Catalog cannot be
    /// reached the result is empty rather than an exception, so a reader still gets their
    /// shelf with the book details missing. Losing covers is a degraded shelf; losing the
    /// shelf is a broken application.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, CatalogBook>> TryFindBooksAsync(
        IReadOnlyCollection<Guid> bookIds,
        CancellationToken cancellationToken)
    {
        if (bookIds.Count == 0)
        {
            return new Dictionary<Guid, CatalogBook>();
        }

        try
        {
            var query = string.Join("&", bookIds.Select(id => $"ids={id}"));
            var books = await httpClient
                .GetFromJsonAsync<IReadOnlyList<CatalogBook>>($"api/books?{query}", cancellationToken);

            return books?.ToDictionary(book => book.Id) ?? new Dictionary<Guid, CatalogBook>();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not reach Catalog for book details; returning entries without them");
            return new Dictionary<Guid, CatalogBook>();
        }
    }
}

public sealed class CatalogOptions
{
    public const string SectionName = "Catalog";

    public Uri BaseAddress { get; set; } = new("http://localhost:5103/");
}
