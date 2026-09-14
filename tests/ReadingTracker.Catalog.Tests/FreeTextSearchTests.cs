using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Catalog.Tests;

/// <summary>
/// One box, whatever the reader types in it: a title, an author, an ISBN, or some of each.
/// Catalog passes words through to the providers' own free-text search and recognises an
/// ISBN by its shape, so the reader never has to say which kind of thing they typed.
/// </summary>
[Collection(CatalogApiCollection.Name)]
public sealed class FreeTextSearchTests(CatalogApiFixture fixture)
{
    private const string GoogleNoMatches = """{ "kind": "books#volumes", "totalItems": 0 }""";

    private static string GoogleMatches(params string[] titles) => $$"""
        {
          "kind": "books#volumes",
          "totalItems": {{titles.Length}},
          "items": [
            {{string.Join(",", titles.Select(title => $$"""
            {
              "id": "vol-q-{{title}}",
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
    public async Task Sends_the_words_to_google_as_they_are_rather_than_as_a_title()
    {
        fixture.GoogleBooks.Respond = _ => StubHttpMessageHandler.Json(GoogleMatches("The Left Hand of Darkness"));

        var results = (await fixture.CreateClient()
            .GetFromJsonAsync<SearchResponse>("/api/books/search?q=le%20guin%20darkness"))!.Results;

        // "le guin darkness" is an author and half a title. Forced into intitle: it matches
        // nothing; sent as it is, Google's own search takes it across every field.
        var sentQuery = Uri.UnescapeDataString(fixture.GoogleBooks.LastRequest!.RequestUri!.Query);
        Assert.Contains("q=le guin darkness", sentQuery);
        Assert.DoesNotContain("intitle:", sentQuery);
        Assert.DoesNotContain("inauthor:", sentQuery);
        Assert.Single(results);
    }

    [Fact]
    public async Task Sends_the_words_to_open_library_as_its_free_text_query()
    {
        fixture.GoogleBooks.Respond = _ => StubHttpMessageHandler.Json(GoogleNoMatches);
        fixture.OpenLibrary.Respond = _ => StubHttpMessageHandler.Json("""
            {
              "numFound": 1,
              "docs": [
                {
                  "key": "/works/OL59757W",
                  "title": "The Dispossessed",
                  "author_name": ["Ursula K. Le Guin"],
                  "number_of_pages_median": 341
                }
              ]
            }
            """);

        var results = (await fixture.CreateClient()
            .GetFromJsonAsync<SearchResponse>("/api/books/search?q=dispossessed%20le%20guin&page=2&pageSize=5"))!.Results;

        var sentQuery = Uri.UnescapeDataString(fixture.OpenLibrary.LastRequest!.RequestUri!.Query);
        Assert.Contains("q=dispossessed le guin", sentQuery);
        Assert.DoesNotContain("title=", sentQuery);
        Assert.DoesNotContain("author=", sentQuery);
        // The window travels with it, in Open Library's own terms.
        Assert.Contains("limit=5", sentQuery);
        Assert.Contains("offset=5", sentQuery);
        Assert.Equal("The Dispossessed", Assert.Single(results).Title);
    }

    // ISBNs no other test uses: an ISBN the database already holds is answered from the cache
    // without asking a provider, and the database outlives any one test.
    [Theory]
    [InlineData("9781916338302", "9781916338302")]
    [InlineData("978-1-916338-30-2", "9781916338302")]
    [InlineData("191633830X", "191633830X")]
    [InlineData("1-916338-30-x", "191633830X")]
    [InlineData(" 9781916338302 ", "9781916338302")]
    public async Task Recognises_an_isbn_by_its_shape_and_looks_it_up_exactly(string typed, string isbn)
    {
        fixture.GoogleBooks.Respond = _ => StubHttpMessageHandler.Json(GoogleNoMatches);

        await fixture.CreateClient().GetAsync($"/api/books/search?q={Uri.EscapeDataString(typed)}");

        // Hyphens and spaces are how people write ISBNs; the provider wants the digits.
        var sentQuery = Uri.UnescapeDataString(fixture.GoogleBooks.LastRequest!.RequestUri!.Query);
        Assert.Contains($"q=isbn:{isbn}", sentQuery);
    }

    [Fact]
    public async Task A_run_of_digits_that_is_not_an_isbn_is_searched_for_as_words()
    {
        fixture.GoogleBooks.Respond = _ => StubHttpMessageHandler.Json(GoogleMatches("1984"));

        await fixture.CreateClient().GetAsync("/api/books/search?q=1984");

        // Four digits is a title, not an ISBN. Ten or thirteen would have been.
        var sentQuery = Uri.UnescapeDataString(fixture.GoogleBooks.LastRequest!.RequestUri!.Query);
        Assert.Contains("q=1984", sentQuery);
        Assert.DoesNotContain("isbn:", sentQuery);
    }

    [Fact]
    public async Task An_isbn_search_is_never_paged()
    {
        fixture.GoogleBooks.Respond = _ => StubHttpMessageHandler.Json(GoogleNoMatches);

        var response = (await fixture.CreateClient()
            .GetFromJsonAsync<SearchResponse>("/api/books/search?q=9781916338319&page=3"))!;

        // One ISBN, one edition: there is no third page of it, so it does not claim one.
        Assert.False(response.HasMore);
        Assert.Contains("startIndex=0", fixture.GoogleBooks.LastRequest!.RequestUri!.Query);
    }

    [Fact]
    public async Task Rejects_a_query_that_is_only_whitespace()
    {
        var response = await fixture.CreateClient().GetAsync("/api/books/search?q=%20%20");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record SearchResponse(IReadOnlyList<Book> Results, int Page, int PageSize, bool HasMore);

    private sealed record Book(Guid Id, string Title, int? TotalPages, string Source);
}
