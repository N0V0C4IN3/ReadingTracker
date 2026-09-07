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
public sealed class CatalogClient(HttpClient httpClient)
{
    /// <summary>Returns null when Catalog has no such Book.</summary>
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
}

public sealed class CatalogOptions
{
    public const string SectionName = "Catalog";

    public Uri BaseAddress { get; set; } = new("http://localhost:5103/");
}
