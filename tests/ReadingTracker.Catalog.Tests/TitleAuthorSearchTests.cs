using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Catalog.Tests;

[Collection(CatalogApiCollection.Name)]
public sealed class TitleAuthorSearchTests(CatalogApiFixture fixture)
{
    private const string GoogleNoMatches = """{ "kind": "books#volumes", "totalItems": 0 }""";

    private static string GoogleMatches(params string[] titles) => $$"""
        {
          "kind": "books#volumes",
          "totalItems": {{titles.Length}},
          "items": [
            {{string.Join(",", titles.Select((title, index) => $$"""
            {
              "id": "vol-{{index}}",
              "volumeInfo": {
                "title": "{{title}}",
                "authors": ["Ursula K. Le Guin"],
                "pageCount": 200
              }
            }
            """))}}
          ]
        }
        """;

    [Fact]
    public async Task Finds_candidates_by_title_alone()
    {
        fixture.GoogleBooks.Respond = _ => StubHttpMessageHandler.Json(GoogleMatches("The Dispossessed", "The Dispossessed: A Novel"));

        var results = (await fixture.CreateClient()
            .GetFromJsonAsync<SearchResponse>("/api/books/search?title=The%20Dispossessed"))!.Results;

        Assert.Equal(2, results.Count);
        Assert.All(results, book => Assert.Contains("Dispossessed", book.Title));
    }

    [Fact]
    public async Task Asks_the_provider_to_narrow_by_author_when_one_is_given()
    {
        fixture.GoogleBooks.Respond = _ => StubHttpMessageHandler.Json(GoogleMatches("A Wizard of Earthsea"));

        await fixture.CreateClient().GetAsync("/api/books/search?title=Earthsea&author=Le%20Guin");

        // The author has to actually reach the provider, or it isn't narrowing anything.
        var sentQuery = Uri.UnescapeDataString(fixture.GoogleBooks.LastRequest!.RequestUri!.Query);
        Assert.Contains("intitle:Earthsea", sentQuery);
        Assert.Contains("inauthor:Le Guin", sentQuery);
    }

    [Fact]
    public async Task Falls_back_to_the_second_provider_for_title_search_too()
    {
        fixture.GoogleBooks.Respond = _ => StubHttpMessageHandler.Json(GoogleNoMatches);
        fixture.OpenLibrary.Respond = _ => StubHttpMessageHandler.Json("""
            {
              "numFound": 1,
              "docs": [
                {
                  "key": "/works/OL453936W",
                  "title": "The Lathe of Heaven",
                  "author_name": ["Ursula K. Le Guin"],
                  "isbn": ["9780380014422"],
                  "number_of_pages_median": 184,
                  "cover_i": 8231856
                }
              ]
            }
            """);

        var results = (await fixture.CreateClient()
            .GetFromJsonAsync<SearchResponse>("/api/books/search?title=Lathe%20of%20Heaven"))!.Results;

        var book = Assert.Single(results);
        Assert.Equal("The Lathe of Heaven", book.Title);
        Assert.Equal("OpenLibrary", book.Source);
        Assert.Equal(184, book.TotalPages);
    }

    [Fact]
    public async Task Rejects_a_search_with_nothing_to_search_for()
    {
        var response = await fixture.CreateClient().GetAsync("/api/books/search");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record SearchResponse(IReadOnlyList<Book> Results);

    private sealed record Book(Guid Id, string Title, int? TotalPages, string Source);
}
