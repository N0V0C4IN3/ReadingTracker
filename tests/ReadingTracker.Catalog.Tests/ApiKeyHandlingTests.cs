using System.Net.Http.Json;

namespace ReadingTracker.Catalog.Tests;

/// <summary>
/// The Google Books key is a secret, and a request URI is not a place secrets survive: it is
/// what proxies, access logs and exception messages all quote. The key travels as a header,
/// which none of them do. The framework's request logging, which Catalog runs at Information,
/// redacts the query string on its own (.NET 10 logs "volumes?*"); the second test is what
/// notices if a future version, or a logging change here, stops that.
/// </summary>
[Collection(CatalogApiCollection.Name)]
public sealed class ApiKeyHandlingTests(CatalogApiFixture fixture)
{
    private const string GoogleNoMatches = """{ "kind": "books#volumes", "totalItems": 0 }""";

    [Fact]
    public async Task Sends_the_key_to_google_as_a_header_and_never_in_the_uri()
    {
        fixture.GoogleBooks.Respond = _ => StubHttpMessageHandler.Json(GoogleNoMatches);

        await fixture.CreateClient().GetFromJsonAsync<SearchResponse>("/api/books/search?q=key%20handling");

        var request = fixture.GoogleBooks.LastRequest!;
        Assert.Equal(
            CatalogApiFixture.GoogleBooksApiKey,
            Assert.Single(request.Headers.GetValues("X-Goog-Api-Key")));
        Assert.DoesNotContain(CatalogApiFixture.GoogleBooksApiKey, request.RequestUri!.ToString());
        Assert.DoesNotContain("key=", request.RequestUri.Query);
    }

    [Fact]
    public async Task Never_writes_the_key_to_its_logs()
    {
        fixture.GoogleBooks.Respond = _ => StubHttpMessageHandler.Json(GoogleNoMatches);

        await fixture.CreateClient().GetFromJsonAsync<SearchResponse>("/api/books/search?q=key%20in%20logs");

        Assert.NotEmpty(fixture.Logs);
        Assert.DoesNotContain(fixture.Logs, line => line.Contains(CatalogApiFixture.GoogleBooksApiKey));
    }

    private sealed record SearchResponse(IReadOnlyList<Book> Results);

    private sealed record Book(Guid Id, string Title);
}
