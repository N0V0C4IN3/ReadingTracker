using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Catalog.Tests;

public sealed class ProviderFallbackTests(CatalogApiFixture fixture) : IClassFixture<CatalogApiFixture>
{
    private const string GoogleNoMatches = """{ "kind": "books#volumes", "totalItems": 0 }""";

    private static string OpenLibraryMatch(string isbn) => $$"""
        {
          "ISBN:{{isbn}}": {
            "key": "/books/OL17952222M",
            "title": "The Word for World Is Forest",
            "authors": [{ "name": "Ursula K. Le Guin" }],
            "number_of_pages": 189,
            "cover": { "large": "https://covers.openlibrary.test/large.jpg" }
          }
        }
        """;

    [Fact]
    public async Task Falls_back_to_the_second_provider_when_the_first_has_no_match()
    {
        const string isbn = "9780765324641";
        fixture.GoogleBooks.Respond = _ => StubHttpMessageHandler.Json(GoogleNoMatches);
        fixture.OpenLibrary.Respond = _ => StubHttpMessageHandler.Json(OpenLibraryMatch(isbn));

        var results = (await fixture.CreateClient()
            .GetFromJsonAsync<SearchResponse>($"/api/books/search?isbn={isbn}"))!.Results;

        var book = Assert.Single(results);
        Assert.Equal("The Word for World Is Forest", book.Title);
        Assert.Equal(189, book.TotalPages);
        Assert.Equal("OpenLibrary", book.Source);
    }

    [Fact]
    public async Task Falls_back_to_the_second_provider_when_the_first_cannot_be_reached()
    {
        const string isbn = "9780060512804";
        fixture.GoogleBooks.Respond = _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        fixture.OpenLibrary.Respond = _ => StubHttpMessageHandler.Json(OpenLibraryMatch(isbn));

        var response = await fixture.CreateClient().GetAsync($"/api/books/search?isbn={isbn}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var book = Assert.Single((await response.Content.ReadFromJsonAsync<SearchResponse>())!.Results);
        Assert.Equal("OpenLibrary", book.Source);
    }

    [Fact]
    public async Task Reports_no_results_when_neither_provider_has_the_isbn()
    {
        fixture.GoogleBooks.Respond = _ => StubHttpMessageHandler.Json(GoogleNoMatches);
        fixture.OpenLibrary.Respond = _ => StubHttpMessageHandler.Json("{}");

        var response = await fixture.CreateClient().GetAsync("/api/books/search?isbn=9780000000001");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<SearchResponse>())!.Results);
    }

    [Fact]
    public async Task Reports_unavailable_rather_than_no_results_when_every_provider_is_down()
    {
        fixture.GoogleBooks.Respond = _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        fixture.OpenLibrary.Respond = _ => new HttpResponseMessage(HttpStatusCode.BadGateway);

        var response = await fixture.CreateClient().GetAsync("/api/books/search?isbn=9780000000002");

        // Telling a reader "no results" here would claim the book doesn't exist, which we don't know.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private sealed record SearchResponse(IReadOnlyList<Book> Results);

    private sealed record Book(Guid Id, string Title, int? TotalPages, string Source);
}
