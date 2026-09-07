using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Catalog.Tests;

public sealed class IsbnSearchTests(CatalogApiFixture fixture) : IClassFixture<CatalogApiFixture>
{
    // Trimmed from a real Google Books volumes response for ISBN 9780441013593.
    private const string DuneVolume = """
        {
          "kind": "books#volumes",
          "totalItems": 1,
          "items": [
            {
              "id": "B1hSG45JCX4C",
              "volumeInfo": {
                "title": "Dune",
                "authors": ["Frank Herbert"],
                "industryIdentifiers": [
                  { "type": "ISBN_13", "identifier": "9780441013593" }
                ],
                "pageCount": 604,
                "imageLinks": {
                  "smallThumbnail": "http://books.google.com/books/content?id=B1hSG45JCX4C&printsec=frontcover&img=1&zoom=5",
                  "thumbnail": "http://books.google.com/books/content?id=B1hSG45JCX4C&printsec=frontcover&img=1&zoom=1"
                }
              }
            }
          ]
        }
        """;

    // Google omits "items" entirely when nothing matches, rather than returning an empty array.
    private const string NoMatches = """
        { "kind": "books#volumes", "totalItems": 0 }
        """;

    [Fact]
    public async Task Returns_the_matching_book_when_the_provider_knows_the_isbn()
    {
        fixture.GoogleBooks.Respond = _ => StubHttpMessageHandler.Json(DuneVolume);

        var response = await fixture.CreateClient().GetAsync("/api/books/search?isbn=9780441013593");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<SearchResponse>();
        var book = Assert.Single(results!.Results);
        Assert.Equal("Dune", book.Title);
        Assert.Equal(["Frank Herbert"], book.Authors);
        Assert.Equal("9780441013593", book.Isbn);
        Assert.Equal(604, book.TotalPages);
        Assert.False(string.IsNullOrWhiteSpace(book.CoverUrl));
    }

    [Fact]
    public async Task Returns_no_results_when_the_provider_has_never_heard_of_the_isbn()
    {
        fixture.GoogleBooks.Respond = _ => StubHttpMessageHandler.Json(NoMatches);

        var response = await fixture.CreateClient().GetAsync("/api/books/search?isbn=9999999999999");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<SearchResponse>();
        Assert.Empty(results!.Results);
    }

    private sealed record SearchResponse(IReadOnlyList<Book> Results);

    private sealed record Book(string Title, IReadOnlyList<string> Authors, string? Isbn, string? CoverUrl, int? TotalPages);
}
