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

        var (page, problem) = await client.SearchAsync(isbn: null, title: "Lathe of Heaven", author: null, page: 1, CancellationToken.None);

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

        var (page, problem) = await client.SearchAsync(isbn: null, title: "Nothing Like This Exists", author: null, page: 1, CancellationToken.None);

        Assert.Null(problem);
        Assert.Empty(page!.Results);
    }

    [Fact]
    public async Task Reports_when_no_book_data_provider_could_be_reached()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        var (page, problem) = await client.SearchAsync(isbn: null, title: "Anything", author: null, page: 1, CancellationToken.None);

        Assert.Null(page);
        Assert.Equal(SearchUnavailable.ProvidersUnavailable, problem);
    }

    [Fact]
    public async Task Reports_when_the_session_has_ended()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        var (page, problem) = await client.SearchAsync(isbn: null, title: "Anything", author: null, page: 1, CancellationToken.None);

        Assert.Null(page);
        Assert.Equal(SearchUnavailable.NotSignedIn, problem);
    }

    [Fact]
    public async Task Reports_when_the_gateway_cannot_be_reached_at_all()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => throw new HttpRequestException("connection refused");

        var (page, problem) = await client.SearchAsync(isbn: null, title: "Anything", author: null, page: 1, CancellationToken.None);

        Assert.Null(page);
        Assert.Equal(SearchUnavailable.GatewayUnreachable, problem);
    }

    [Fact]
    public async Task Searches_by_isbn_title_and_author_together()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => StubHttpMessageHandler.Json("""{ "results": [], "page": 1, "pageSize": 10, "hasMore": false }""");

        await client.SearchAsync(isbn: "9780380014422", title: "Lathe", author: "Le Guin", page: 1, CancellationToken.None);

        var query = gateway.LastRequest!.RequestUri!.Query;
        Assert.Contains("isbn=9780380014422", query);
        Assert.Contains("title=Lathe", query);
        Assert.Contains("author=Le", query);
    }

    [Fact]
    public async Task Asks_the_catalog_for_the_page_it_was_given()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => StubHttpMessageHandler.Json("""{ "results": [], "page": 4, "pageSize": 10, "hasMore": false }""");

        await client.SearchAsync(isbn: null, title: null, author: "Le Guin", page: 4, CancellationToken.None);

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

        var (page, _) = await client.SearchAsync(isbn: null, title: null, author: "Le Guin", page: 2, CancellationToken.None);

        Assert.Equal(2, page!.Page);
        Assert.True(page.HasMore);
    }

    private static (GatewayCatalogClient Client, StubHttpMessageHandler Gateway) CreateClient()
    {
        var gateway = new StubHttpMessageHandler();
        var httpClient = new HttpClient(gateway) { BaseAddress = new Uri("http://gateway.test/") };
        return (new GatewayCatalogClient(httpClient), gateway);
    }
}
