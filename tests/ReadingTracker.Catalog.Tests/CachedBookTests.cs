using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Catalog.Tests;

public sealed class CachedBookTests(CatalogApiFixture fixture) : IClassFixture<CatalogApiFixture>
{
    private static string VolumeFor(string isbn, string volumeId, string title) => $$"""
        {
          "kind": "books#volumes",
          "totalItems": 1,
          "items": [
            {
              "id": "{{volumeId}}",
              "volumeInfo": {
                "title": "{{title}}",
                "authors": ["Ursula K. Le Guin"],
                "industryIdentifiers": [{ "type": "ISBN_13", "identifier": "{{isbn}}" }],
                "pageCount": 384,
                "imageLinks": { "thumbnail": "http://example.test/cover.jpg" }
              }
            }
          ]
        }
        """;

    [Fact]
    public async Task Serves_a_repeated_isbn_search_from_the_cache_without_asking_the_provider_again()
    {
        const string isbn = "9780441478125";
        fixture.GoogleBooks.Respond = _ =>
            StubHttpMessageHandler.Json(VolumeFor(isbn, "leguin-1", "The Left Hand of Darkness"));
        var client = fixture.CreateClient();

        var first = await client.GetFromJsonAsync<SearchResponse>($"/api/books/search?isbn={isbn}");
        var callsAfterFirstSearch = fixture.GoogleBooks.RequestCount;

        var second = await client.GetFromJsonAsync<SearchResponse>($"/api/books/search?isbn={isbn}");

        Assert.Equal(callsAfterFirstSearch, fixture.GoogleBooks.RequestCount);
        // The same Book comes back, rather than a second copy of it.
        Assert.Equal(Assert.Single(first!.Results).Id, Assert.Single(second!.Results).Id);
    }

    [Fact]
    public async Task Returns_a_book_that_was_cached_by_an_earlier_search()
    {
        const string isbn = "9780441569595";
        fixture.GoogleBooks.Respond = _ =>
            StubHttpMessageHandler.Json(VolumeFor(isbn, "leguin-2", "The Dispossessed"));
        var client = fixture.CreateClient();

        var found = Assert.Single((await client.GetFromJsonAsync<SearchResponse>($"/api/books/search?isbn={isbn}"))!.Results);

        var response = await client.GetAsync($"/api/books/{found.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var book = await response.Content.ReadFromJsonAsync<Book>();
        Assert.Equal(found.Id, book!.Id);
        Assert.Equal("The Dispossessed", book.Title);
        Assert.Equal(isbn, book.Isbn);
        Assert.Equal(384, book.TotalPages);
    }

    [Fact]
    public async Task Reports_not_found_for_a_book_id_that_was_never_cached()
    {
        var response = await fixture.CreateClient().GetAsync($"/api/books/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Stores_one_book_when_the_provider_returns_the_same_isbn_twice()
    {
        const string isbn = "9780441788385";
        // Providers do return duplicate editions of one ISBN in a single response.
        fixture.GoogleBooks.Respond = _ => StubHttpMessageHandler.Json($$"""
            {
              "kind": "books#volumes",
              "totalItems": 2,
              "items": [
                {
                  "id": "wizard-a",
                  "volumeInfo": {
                    "title": "A Wizard of Earthsea",
                    "authors": ["Ursula K. Le Guin"],
                    "industryIdentifiers": [{ "type": "ISBN_13", "identifier": "{{isbn}}" }],
                    "pageCount": 183
                  }
                },
                {
                  "id": "wizard-b",
                  "volumeInfo": {
                    "title": "A Wizard of Earthsea",
                    "authors": ["Ursula K. Le Guin"],
                    "industryIdentifiers": [{ "type": "ISBN_13", "identifier": "{{isbn}}" }],
                    "pageCount": 183
                  }
                }
              ]
            }
            """);

        var results = (await fixture.CreateClient().GetFromJsonAsync<SearchResponse>($"/api/books/search?isbn={isbn}"))!.Results;

        Assert.Single(results);
    }

    private sealed record SearchResponse(IReadOnlyList<Book> Results);

    private sealed record Book(
        Guid Id,
        string Title,
        IReadOnlyList<string> Authors,
        string? Isbn,
        string? CoverUrl,
        int? TotalPages);
}
