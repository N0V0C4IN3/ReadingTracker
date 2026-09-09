using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Catalog.Tests;

[Collection(CatalogApiCollection.Name)]
public sealed class TitleAuthorSearchTests(CatalogApiFixture fixture)
{
    private const string GoogleNoMatches = """{ "kind": "books#volumes", "totalItems": 0 }""";

    // The volume id is Catalog's identity for a Book with no ISBN, and the database outlives any
    // one test, so it follows the title: an id reused across tests would hand the second test the
    // first one's Book, title and all.
    private static string GoogleMatches(params string[] titles) => $$"""
        {
          "kind": "books#volumes",
          "totalItems": {{titles.Length}},
          "items": [
            {{string.Join(",", titles.Select(title => $$"""
            {
              "id": "vol-{{title}}",
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

    [Fact]
    public async Task Asks_google_for_the_window_the_reader_paged_to()
    {
        fixture.GoogleBooks.Respond = _ => StubHttpMessageHandler.Json(GoogleMatches("The Dispossessed"));

        await fixture.CreateClient().GetAsync("/api/books/search?author=Le%20Guin&page=3&pageSize=5");

        // Paging has to reach the provider, or the third page is the first one again.
        var sentQuery = fixture.GoogleBooks.LastRequest!.RequestUri!.Query;
        Assert.Contains("startIndex=10", sentQuery);
        Assert.Contains("maxResults=5", sentQuery);
    }

    [Fact]
    public async Task Asks_open_library_for_the_same_window_in_its_own_terms()
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
                  "number_of_pages_median": 184
                }
              ]
            }
            """);

        await fixture.CreateClient().GetAsync("/api/books/search?author=Le%20Guin&page=2&pageSize=5");

        var sentQuery = fixture.OpenLibrary.LastRequest!.RequestUri!.Query;
        Assert.Contains("limit=5", sentQuery);
        Assert.Contains("offset=5", sentQuery);
    }

    [Fact]
    public async Task Says_there_is_more_when_the_page_came_back_full()
    {
        fixture.GoogleBooks.Respond = _ => StubHttpMessageHandler.Json(GoogleMatches("Earthsea I", "Earthsea II"));

        var page = (await fixture.CreateClient()
            .GetFromJsonAsync<SearchResponse>("/api/books/search?author=Le%20Guin&page=2&pageSize=2"))!;

        Assert.True(page.HasMore);
        Assert.Equal(2, page.Page);
        Assert.Equal(2, page.PageSize);
    }

    [Fact]
    public async Task Says_there_is_no_more_when_the_page_came_back_short()
    {
        fixture.GoogleBooks.Respond = _ => StubHttpMessageHandler.Json(GoogleMatches("Earthsea I", "Earthsea II"));

        var page = (await fixture.CreateClient()
            .GetFromJsonAsync<SearchResponse>("/api/books/search?author=Le%20Guin&pageSize=5"))!;

        Assert.False(page.HasMore);
        Assert.Equal(1, page.Page);
    }

    [Fact]
    public async Task Still_says_there_is_more_when_duplicates_shrank_a_full_page()
    {
        // Google listing the same volume twice is one Book to us, but it still filled the window
        // it was given — a page that de-duplicated down to one is not the end of the results.
        fixture.GoogleBooks.Respond = _ => StubHttpMessageHandler.Json("""
            {
              "kind": "books#volumes",
              "totalItems": 2,
              "items": [
                { "id": "vol-twice", "volumeInfo": { "title": "The Dispossessed", "authors": ["Ursula K. Le Guin"] } },
                { "id": "vol-twice", "volumeInfo": { "title": "The Dispossessed", "authors": ["Ursula K. Le Guin"] } }
              ]
            }
            """);

        var page = (await fixture.CreateClient()
            .GetFromJsonAsync<SearchResponse>("/api/books/search?title=Dispossessed&pageSize=2"))!;

        Assert.Single(page.Results);
        Assert.True(page.HasMore);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=41")]
    public async Task Rejects_a_page_that_cannot_exist(string paging)
    {
        var response = await fixture.CreateClient().GetAsync($"/api/books/search?title=Earthsea&{paging}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record SearchResponse(IReadOnlyList<Book> Results, int Page, int PageSize, bool HasMore);

    private sealed record Book(Guid Id, string Title, int? TotalPages, string Source);
}
