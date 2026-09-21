using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace ReadingTracker.Catalog.Books;

public sealed class GoogleBooksProvider(HttpClient httpClient, IOptions<GoogleBooksOptions> options) : IBookProvider
{
    private readonly GoogleBooksOptions _options = options.Value;

    public BookSource Source => BookSource.GoogleBooks;

    public Task<IReadOnlyList<BookSearchResult>> SearchByIsbnAsync(
        string isbn,
        CancellationToken cancellationToken) =>
        SearchAsync($"isbn:{isbn}", isbn, SearchWindow.First, cancellationToken);

    public Task<IReadOnlyList<BookSearchResult>> SearchByTitleAndAuthorAsync(
        string? title,
        string? author,
        SearchWindow window,
        CancellationToken cancellationToken)
    {
        var terms = new List<string>(2);

        if (!string.IsNullOrWhiteSpace(title))
        {
            terms.Add($"intitle:{title.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(author))
        {
            terms.Add($"inauthor:{author.Trim()}");
        }

        return terms.Count == 0
            ? Task.FromResult<IReadOnlyList<BookSearchResult>>([])
            : SearchAsync(string.Join(' ', terms), searchedIsbn: null, window, cancellationToken);
    }

    /// <summary>Unqualified words: Google's own search reads them across title, author and the rest.</summary>
    public Task<IReadOnlyList<BookSearchResult>> SearchAsync(
        string query,
        SearchWindow window,
        CancellationToken cancellationToken) =>
        SearchAsync(query.Trim(), searchedIsbn: null, window, cancellationToken);

    /// <summary>
    /// One volume by Google's id for it. Google answers a volume it no longer has with a 404,
    /// which is an answer; anything else that is not success is treated as not reaching it.
    /// </summary>
    public async Task<BookDetails?> FindDetailsAsync(string externalId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync($"volumes/{Uri.EscapeDataString(externalId)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var volume = await response.Content.ReadFromJsonAsync<VolumeItem>(cancellationToken);

        return volume is null ? null : DetailsOf(volume.VolumeInfo);
    }

    public Uri PageFor(string externalId) => new($"https://books.google.com/books?id={Uri.EscapeDataString(externalId)}");

    private async Task<IReadOnlyList<BookSearchResult>> SearchAsync(
        string searchTerms,
        string? searchedIsbn,
        SearchWindow window,
        CancellationToken cancellationToken)
    {
        // Google pages by offset rather than by page number, which is why SearchWindow exposes one.
        var query = $"volumes?q={Uri.EscapeDataString(searchTerms)}"
            + $"&startIndex={window.Offset}&maxResults={window.PageSize}";

        using var response = await SendAsync(query, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<VolumesResponse>(cancellationToken);

        // Google omits "items" entirely when nothing matches, rather than returning an empty array.
        return payload?.Items?.Select(item => ToSearchResult(item, searchedIsbn)).ToArray() ?? [];
    }

    private async Task<HttpResponseMessage> SendAsync(string query, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, query);

        // As a header, never as a query parameter: a URI is what proxies, access logs and
        // exception messages quote, and a key in one is a key in all of them. Google accepts
        // either; only one of them keeps the secret out of the address.
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.Add("X-Goog-Api-Key", _options.ApiKey);
        }

        return await httpClient.SendAsync(request, cancellationToken);
    }

    private static BookSearchResult ToSearchResult(VolumeItem item, string? searchedIsbn)
    {
        var info = item.VolumeInfo;

        return new BookSearchResult(
            Title: info?.Title ?? string.Empty,
            Authors: info?.Authors ?? [],
            Isbn: PreferredIsbn(info?.IndustryIdentifiers) ?? searchedIsbn,
            CoverUrl: info?.ImageLinks?.Thumbnail ?? info?.ImageLinks?.SmallThumbnail,
            // Google says 0 when it does not know; that is no page count, not a short book.
            TotalPages: info?.PageCount is > 0 and var pages ? pages : null,
            Source: BookSource.GoogleBooks,
            ExternalId: item.Id,
            // A volume list carries the same details as the volume itself, so a Book cached
            // from a search never needs to be asked about again.
            Details: DetailsOf(info));
    }

    private static BookDetails DetailsOf(VolumeInfo? info) =>
        BookDetails.Cleaned(info?.Description, info?.Publisher, info?.PublishedDate, info?.Categories);

    private static string? PreferredIsbn(IReadOnlyList<IndustryIdentifier>? identifiers) =>
        identifiers?.FirstOrDefault(id => id.Type == "ISBN_13")?.Identifier
        ?? identifiers?.FirstOrDefault(id => id.Type == "ISBN_10")?.Identifier;

    // Google Books' response shape, narrowed to the fields Catalog actually uses.
    private sealed record VolumesResponse(IReadOnlyList<VolumeItem>? Items);

    private sealed record VolumeItem(string? Id, VolumeInfo? VolumeInfo);

    private sealed record VolumeInfo(
        string? Title,
        IReadOnlyList<string>? Authors,
        IReadOnlyList<IndustryIdentifier>? IndustryIdentifiers,
        int? PageCount,
        ImageLinks? ImageLinks,
        string? Description,
        string? Publisher,
        string? PublishedDate,
        IReadOnlyList<string>? Categories);

    private sealed record IndustryIdentifier(string? Type, string? Identifier);

    private sealed record ImageLinks(string? SmallThumbnail, string? Thumbnail);
}
