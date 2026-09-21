using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
    public BookSource Source => BookSource.OpenLibrary;

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
        SearchWindow window,
        CancellationToken cancellationToken)
    {
        var terms = new List<string>(4)
        {
            "fields=key,title,author_name,isbn,number_of_pages_median,cover_i",
            $"limit={window.PageSize}",
            $"offset={window.Offset}",
        };

        if (!string.IsNullOrWhiteSpace(title))
        {
            terms.Add($"title={Uri.EscapeDataString(title.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(author))
        {
            terms.Add($"author={Uri.EscapeDataString(author.Trim())}");
        }

        // Nothing but the fields and the window: no title and no author is nothing to search for.
        if (terms.Count == 3)
        {
            return [];
        }

        return await SearchAsync(terms, cancellationToken);
    }

    /// <summary>Unqualified words go to Open Library's general query, which reads every field.</summary>
    public Task<IReadOnlyList<BookSearchResult>> SearchAsync(
        string query,
        SearchWindow window,
        CancellationToken cancellationToken) =>
        SearchAsync(
            [
                "fields=key,title,author_name,isbn,number_of_pages_median,cover_i",
                $"limit={window.PageSize}",
                $"offset={window.Offset}",
                $"q={Uri.EscapeDataString(query.Trim())}",
            ],
            cancellationToken);

    /// <summary>
    /// Open Library's id for a book is a key — <c>/works/OL…W</c> from a search, <c>/books/OL…M</c>
    /// from an ISBN lookup — and the record behind it is at that key. An edition's record
    /// names its publisher and date but often leaves the description and subjects to the work
    /// it belongs to, so an edition without them is followed to its work.
    /// </summary>
    public async Task<BookDetails?> FindDetailsAsync(string externalId, CancellationToken cancellationToken)
    {
        var record = await FetchAsync(externalId, cancellationToken);

        if (record is null)
        {
            return null;
        }

        var description = record.Description;
        var subjects = record.Subjects;

        if ((description is null || subjects is null) && record.Works?.FirstOrDefault()?.Key is { } workKey)
        {
            var work = await FetchAsync(workKey, cancellationToken);
            description ??= work?.Description;
            subjects ??= work?.Subjects;
        }

        return BookDetails.Cleaned(
            TextOf(description),
            record.Publishers?.FirstOrDefault(),
            record.PublishDate ?? record.FirstPublishDate,
            subjects);
    }

    // The site, not the API's base address: those are the same host today, but a reader is
    // being sent to a page, and a proxy in front of the API is no place to send them.
    public Uri PageFor(string externalId) => new($"https://openlibrary.org/{Path(externalId)}");

    private async Task<OpenLibraryRecord?> FetchAsync(string key, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"{Path(key)}.json", cancellationToken);

        // A key Open Library no longer has is an answer, unlike a failure to reach it at all.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<OpenLibraryRecord>(cancellationToken);
    }

    /// <summary>A key relative to the site, whether or not it was stored with its leading slash.</summary>
    private static string Path(string key) => key.TrimStart('/');

    /// <summary>
    /// Open Library writes a description either as a bare string or as a typed text object
    /// with the string under "value"; both are the same description.
    /// </summary>
    private static string? TextOf(JsonElement? description) => description switch
    {
        { ValueKind: JsonValueKind.String } text => text.GetString(),
        { ValueKind: JsonValueKind.Object } typed when typed.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.String => value.GetString(),
        _ => null,
    };

    private async Task<IReadOnlyList<BookSearchResult>> SearchAsync(
        IReadOnlyList<string> terms,
        CancellationToken cancellationToken)
    {
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
        TotalPages: doc.NumberOfPagesMedian is > 0 and var pages ? pages : null,
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

    /// <summary>A work or an edition record; the fields either may carry.</summary>
    private sealed record OpenLibraryRecord(
        JsonElement? Description,
        IReadOnlyList<string>? Subjects,
        IReadOnlyList<string>? Publishers,
        [property: JsonPropertyName("publish_date")] string? PublishDate,
        [property: JsonPropertyName("first_publish_date")] string? FirstPublishDate,
        IReadOnlyList<OpenLibraryWorkReference>? Works);

    private sealed record OpenLibraryWorkReference(string? Key);
}
