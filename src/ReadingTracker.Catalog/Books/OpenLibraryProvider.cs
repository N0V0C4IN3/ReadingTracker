using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace ReadingTracker.Catalog.Books;

public sealed class OpenLibraryOptions
{
    public const string SectionName = "OpenLibrary";

    public Uri BaseAddress { get; set; } = new("https://openlibrary.org/");
}

/// <summary>
/// Open Library needs no API key, which makes it a useful second opinion when Google Books
/// has no answer. Its metadata is less consistently complete, so it is consulted second.
/// </summary>
public sealed class OpenLibraryProvider(HttpClient httpClient) : IBookProvider
{
    public async Task<IReadOnlyList<BookSearchResult>> SearchByIsbnAsync(
        string isbn,
        CancellationToken cancellationToken)
    {
        var bibliographicKey = $"ISBN:{isbn}";
        var query = $"api/books?bibkeys={Uri.EscapeDataString(bibliographicKey)}&format=json&jscmd=data";

        // Keyed by the bibkey that was asked for; an unknown ISBN comes back as {}.
        var payload = await httpClient
            .GetFromJsonAsync<Dictionary<string, OpenLibraryBook>>(query, cancellationToken);

        if (payload is null || !payload.TryGetValue(bibliographicKey, out var book))
        {
            return [];
        }

        return
        [
            new BookSearchResult(
                Title: book.Title ?? string.Empty,
                Authors: book.Authors?.Select(author => author.Name ?? string.Empty).ToArray() ?? [],
                Isbn: isbn,
                CoverUrl: book.Cover?.Large ?? book.Cover?.Medium ?? book.Cover?.Small,
                TotalPages: book.NumberOfPages,
                Source: BookSource.OpenLibrary,
                ExternalId: book.Key),
        ];
    }

    // Open Library's response shape, narrowed to the fields Catalog actually uses.
    private sealed record OpenLibraryBook(
        string? Key,
        string? Title,
        IReadOnlyList<OpenLibraryAuthor>? Authors,
        [property: JsonPropertyName("number_of_pages")] int? NumberOfPages,
        OpenLibraryCover? Cover);

    private sealed record OpenLibraryAuthor(string? Name);

    private sealed record OpenLibraryCover(string? Small, string? Medium, string? Large);
}
