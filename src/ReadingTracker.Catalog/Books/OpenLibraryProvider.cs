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

    public async Task<IReadOnlyList<BookSearchResult>> SearchByTitleAndAuthorAsync(
        string? title,
        string? author,
        CancellationToken cancellationToken)
    {
        var terms = new List<string>(3) { "fields=key,title,author_name,isbn,number_of_pages_median,cover_i", "limit=10" };

        if (!string.IsNullOrWhiteSpace(title))
        {
            terms.Add($"title={Uri.EscapeDataString(title.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(author))
        {
            terms.Add($"author={Uri.EscapeDataString(author.Trim())}");
        }

        if (terms.Count == 2)
        {
            return [];
        }

        var payload = await httpClient
            .GetFromJsonAsync<OpenLibrarySearch>($"search.json?{string.Join('&', terms)}", cancellationToken);

        return payload?.Docs?.Select(ToSearchResult).ToArray() ?? [];
    }

    private static BookSearchResult ToSearchResult(OpenLibraryDoc doc) => new(
        Title: doc.Title ?? string.Empty,
        Authors: doc.AuthorName ?? [],
        // Open Library lists every edition's ISBN; prefer a 13-digit one.
        Isbn: doc.Isbn?.FirstOrDefault(isbn => isbn.Length == 13) ?? doc.Isbn?.FirstOrDefault(),
        CoverUrl: doc.CoverId is null ? null : $"https://covers.openlibrary.org/b/id/{doc.CoverId}-L.jpg",
        TotalPages: doc.NumberOfPagesMedian,
        Source: BookSource.OpenLibrary,
        ExternalId: doc.Key);

    // Open Library's response shape, narrowed to the fields Catalog actually uses.
    private sealed record OpenLibrarySearch(IReadOnlyList<OpenLibraryDoc>? Docs);

    private sealed record OpenLibraryDoc(
        string? Key,
        string? Title,
        [property: JsonPropertyName("author_name")] IReadOnlyList<string>? AuthorName,
        IReadOnlyList<string>? Isbn,
        [property: JsonPropertyName("number_of_pages_median")] int? NumberOfPagesMedian,
        [property: JsonPropertyName("cover_i")] int? CoverId);

    private sealed record OpenLibraryBook(
        string? Key,
        string? Title,
        IReadOnlyList<OpenLibraryAuthor>? Authors,
        [property: JsonPropertyName("number_of_pages")] int? NumberOfPages,
        OpenLibraryCover? Cover);

    private sealed record OpenLibraryAuthor(string? Name);

    private sealed record OpenLibraryCover(string? Small, string? Medium, string? Large);
}
