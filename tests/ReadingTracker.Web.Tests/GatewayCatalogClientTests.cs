using System.Net;
using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

public sealed class GatewayCatalogClientTests
{
    [Fact]
    public async Task Finds_books_matching_a_search()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => StubHttpMessageHandler.Json("""
            {
              "results": [
                {
                  "id": "11111111-1111-1111-1111-111111111111",
                  "title": "The Lathe of Heaven",
                  "authors": ["Ursula K. Le Guin"],
                  "isbn": "9780380014422",
                  "coverUrl": "https://example.test/cover.jpg",
                  "totalPages": 304,
                  "source": "GoogleBooks"
                }
              ],
              "page": 1,
              "pageSize": 10,
              "hasMore": false
            }
            """);

        var (page, problem) = await client.SearchAsync("Lathe of Heaven", page: 1, CancellationToken.None);

        Assert.Null(problem);
        var book = Assert.Single(page!.Results);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), book.Id);
        Assert.Equal("The Lathe of Heaven", book.Title);
        Assert.Equal(["Ursula K. Le Guin"], book.Authors);
        Assert.Equal("https://example.test/cover.jpg", book.CoverUrl);
        Assert.Equal(304, book.TotalPages);
    }

    [Fact]
    public async Task Distinguishes_no_matches_from_being_unavailable()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => StubHttpMessageHandler.Json("""{ "results": [], "page": 1, "pageSize": 10, "hasMore": false }""");

        var (page, problem) = await client.SearchAsync("Nothing Like This Exists", page: 1, CancellationToken.None);

        Assert.Null(problem);
        Assert.Empty(page!.Results);
    }

    [Fact]
    public async Task Reports_when_no_book_data_provider_could_be_reached()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        var (page, problem) = await client.SearchAsync("Anything", page: 1, CancellationToken.None);

        Assert.Null(page);
        Assert.Equal(SearchUnavailable.ProvidersUnavailable, problem);
    }

    [Fact]
    public async Task Reports_when_the_reader_is_searching_too_fast()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        var (page, problem) = await client.SearchAsync("Anything", page: 1, CancellationToken.None);

        Assert.Null(page);
        Assert.Equal(SearchUnavailable.TooManyRequests, problem);
    }

    [Fact]
    public async Task Reports_when_catalog_refused_to_run_the_search()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.BadRequest);

        var (page, problem) = await client.SearchAsync(new string('q', 500), page: 1, CancellationToken.None);

        Assert.Null(page);
        Assert.Equal(SearchUnavailable.Refused, problem);
    }

    [Fact]
    public async Task Reports_when_the_session_has_ended()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        var (page, problem) = await client.SearchAsync("Anything", page: 1, CancellationToken.None);

        Assert.Null(page);
        Assert.Equal(SearchUnavailable.NotSignedIn, problem);
    }

    [Fact]
    public async Task Reports_when_the_gateway_cannot_be_reached_at_all()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => throw new HttpRequestException("connection refused");

        var (page, problem) = await client.SearchAsync("Anything", page: 1, CancellationToken.None);

        Assert.Null(page);
        Assert.Equal(SearchUnavailable.GatewayUnreachable, problem);
    }

    [Fact]
    public async Task Sends_whatever_was_typed_as_one_query_for_catalog_to_make_sense_of()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => StubHttpMessageHandler.Json("""{ "results": [], "page": 1, "pageSize": 10, "hasMore": false }""");

        await client.SearchAsync("  lathe of heaven le guin ", page: 1, CancellationToken.None);

        // Not split into fields here: Catalog is the one that knows an ISBN from a title.
        var query = Uri.UnescapeDataString(gateway.LastRequest!.RequestUri!.Query);
        Assert.Contains("q=lathe of heaven le guin", query);
        Assert.DoesNotContain("title=", query);
        Assert.DoesNotContain("author=", query);
        Assert.DoesNotContain("isbn=", query);
    }

    [Fact]
    public async Task Asks_the_catalog_for_the_page_it_was_given()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => StubHttpMessageHandler.Json("""{ "results": [], "page": 4, "pageSize": 10, "hasMore": false }""");

        await client.SearchAsync("Le Guin", page: 4, CancellationToken.None);

        Assert.Contains("page=4", gateway.LastRequest!.RequestUri!.Query);
    }

    [Fact]
    public async Task Reads_back_which_page_this_is_and_whether_there_is_another()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => StubHttpMessageHandler.Json("""
            {
              "results": [
                {
                  "id": "22222222-2222-2222-2222-222222222222",
                  "title": "The Word for World Is Forest",
                  "authors": ["Ursula K. Le Guin"],
                  "source": "GoogleBooks"
                }
              ],
              "page": 2,
              "pageSize": 10,
              "hasMore": true
            }
            """);

        var (page, _) = await client.SearchAsync("Le Guin", page: 2, CancellationToken.None);

        Assert.Equal(2, page!.Page);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task Reads_one_book_in_full()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => StubHttpMessageHandler.Json("""
            {
              "id": "22222222-2222-2222-2222-222222222222",
              "title": "Red Rising",
              "authors": ["Pierce Brown"],
              "isbn": "9780345539786",
              "coverUrl": "https://example.test/red-rising.jpg",
              "totalPages": 382,
              "source": "GoogleBooks",
              "description": "Darrow is a Red.\n\nHe works all day.",
              "publisher": "Del Rey",
              "publishedDate": "2014-01-28",
              "categories": ["Fiction", "Science Fiction"],
              "providerUrl": "https://books.google.com/books?id=redrising",
              "detailsUnavailable": false
            }
            """);

        var (book, problem) = await client.GetBookAsync(Guid.Parse("22222222-2222-2222-2222-222222222222"), CancellationToken.None);

        Assert.Null(problem);
        Assert.Equal("api/books/22222222-2222-2222-2222-222222222222", gateway.LastRequest!.RequestUri!.PathAndQuery.TrimStart('/'));
        Assert.Equal("Red Rising", book!.Title);
        Assert.Equal(["Darrow is a Red.", "He works all day."], book.Paragraphs);
        Assert.Equal("Del Rey", book.Publisher);
        Assert.Equal("2014", book.PublishedYear);
        Assert.Equal(["Fiction", "Science Fiction"], book.Categories);
        Assert.Equal("https://books.google.com/books?id=redrising", book.ProviderUrl);
        Assert.Equal("Google Books", book.ProviderName);
        Assert.False(book.DetailsUnavailable);
    }

    [Fact]
    public async Task Reads_a_book_whose_details_could_not_be_fetched()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => StubHttpMessageHandler.Json("""
            {
              "id": "33333333-3333-3333-3333-333333333333",
              "title": "The Word for World Is Forest",
              "authors": ["Ursula K. Le Guin"],
              "source": "OpenLibrary",
              "categories": [],
              "providerUrl": "https://openlibrary.org/books/OL17952222M",
              "detailsUnavailable": true
            }
            """);

        var (book, problem) = await client.GetBookAsync(Guid.Parse("33333333-3333-3333-3333-333333333333"), CancellationToken.None);

        Assert.Null(problem);
        Assert.True(book!.DetailsUnavailable);
        Assert.Empty(book.Paragraphs);
        Assert.Null(book.PublishedYear);
        Assert.Equal("Open Library", book.ProviderName);
    }

    [Fact]
    public async Task Reports_a_book_catalog_does_not_have()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        var (book, problem) = await client.GetBookAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(book);
        Assert.Equal(BookUnavailable.NotFound, problem);
    }

    [Fact]
    public async Task Reports_when_a_book_lookup_finds_the_session_ended()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        var (book, problem) = await client.GetBookAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(book);
        Assert.Equal(BookUnavailable.NotSignedIn, problem);
    }

    [Fact]
    public async Task Reports_when_a_book_lookup_cannot_reach_the_gateway()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => throw new HttpRequestException("connection refused");

        var (book, problem) = await client.GetBookAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(book);
        Assert.Equal(BookUnavailable.GatewayUnreachable, problem);
    }

    private static (GatewayCatalogClient Client, StubHttpMessageHandler Gateway) CreateClient()
    {
        var gateway = new StubHttpMessageHandler();
        var httpClient = new HttpClient(gateway) { BaseAddress = new Uri("http://gateway.test/") };
        return (new GatewayCatalogClient(httpClient), gateway);
    }
}
