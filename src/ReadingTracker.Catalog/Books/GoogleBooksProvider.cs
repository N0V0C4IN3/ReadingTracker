using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace ReadingTracker.Catalog.Books;

public sealed class GoogleBooksProvider(HttpClient httpClient, IOptions<GoogleBooksOptions> options) : IBookProvider
{
    private readonly GoogleBooksOptions _options = options.Value;

    public async Task<IReadOnlyList<BookSearchResult>> SearchByIsbnAsync(
        string isbn,
        CancellationToken cancellationToken)
    {
        var query = $"volumes?q=isbn:{Uri.EscapeDataString(isbn)}";

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            query += $"&key={Uri.EscapeDataString(_options.ApiKey)}";
        }

        var payload = await httpClient.GetFromJsonAsync<VolumesResponse>(query, cancellationToken);

        // Google omits "items" entirely when nothing matches, rather than returning an empty array.
        return payload?.Items?.Select(item => ToSearchResult(item, isbn)).ToArray() ?? [];
    }

    private static BookSearchResult ToSearchResult(VolumeItem item, string searchedIsbn)
    {
        var info = item.VolumeInfo;

        return new BookSearchResult(
            Title: info?.Title ?? string.Empty,
            Authors: info?.Authors ?? [],
            Isbn: PreferredIsbn(info?.IndustryIdentifiers) ?? searchedIsbn,
            CoverUrl: info?.ImageLinks?.Thumbnail ?? info?.ImageLinks?.SmallThumbnail,
            TotalPages: info?.PageCount,
            Source: BookSource.GoogleBooks,
            ExternalId: item.Id);
    }

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
        ImageLinks? ImageLinks);

    private sealed record IndustryIdentifier(string? Type, string? Identifier);

    private sealed record ImageLinks(string? SmallThumbnail, string? Thumbnail);
}
