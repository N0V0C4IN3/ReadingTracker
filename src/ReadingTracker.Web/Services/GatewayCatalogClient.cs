using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Web.Services;

/// <summary>A Book as Catalog (by way of the Gateway) describes a search match.</summary>
public sealed record BookSearchResult(
    Guid Id,
    string Title,
    IReadOnlyList<string> Authors,
    string? CoverUrl,
    int? TotalPages);

/// <summary>
/// Why a catalog search could not be answered. The causes are told apart because they call for
/// different things from the reader: waiting, signing in again, or nothing at all — a real
/// answer of "no matches" is not one of these.
/// </summary>
public enum SearchUnavailable
{
    /// <summary>There is no session, or it has ended. The Gateway said 401.</summary>
    NotSignedIn,

    /// <summary>No book data provider could be reached. This does not mean the book doesn't exist.</summary>
    ProvidersUnavailable,

    /// <summary>The Gateway itself could not be reached — a network failure, not a refusal.</summary>
    GatewayUnreachable,
}

/// <summary>
/// The browser's window onto the Catalog, reached through the Gateway rather than Catalog
/// directly — nothing in this application is allowed to know Catalog's address.
/// </summary>
public sealed class GatewayCatalogClient(HttpClient httpClient)
{
    public async Task<(IReadOnlyList<BookSearchResult>? Results, SearchUnavailable? Problem)> SearchAsync(
        string? isbn,
        string? title,
        string? author,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await httpClient.GetAsync($"api/books/search{BuildQuery(isbn, title, author)}", cancellationToken);
        }
        catch (HttpRequestException)
        {
            // Distinguishable from a real "no matches" answer: nobody answered at all.
            return (null, SearchUnavailable.GatewayUnreachable);
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized)
        {
            return (null, SearchUnavailable.NotSignedIn);
        }

        if (response.StatusCode is HttpStatusCode.ServiceUnavailable)
        {
            return (null, SearchUnavailable.ProvidersUnavailable);
        }

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<SearchResponseBody>(cancellationToken);
        return (body?.Results ?? [], null);
    }

    private static string BuildQuery(string? isbn, string? title, string? author)
    {
        var parameters = new List<string>();

        if (!string.IsNullOrWhiteSpace(isbn))
        {
            parameters.Add($"isbn={Uri.EscapeDataString(isbn)}");
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            parameters.Add($"title={Uri.EscapeDataString(title)}");
        }

        if (!string.IsNullOrWhiteSpace(author))
        {
            parameters.Add($"author={Uri.EscapeDataString(author)}");
        }

        return parameters.Count == 0 ? "" : $"?{string.Join("&", parameters)}";
    }

    private sealed record SearchResponseBody(IReadOnlyList<BookSearchResult> Results);
}
