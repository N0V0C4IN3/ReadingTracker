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
              ]
            }
            """);

        var (results, problem) = await client.SearchAsync(isbn: null, title: "Lathe of Heaven", author: null, CancellationToken.None);

        Assert.Null(problem);
        var book = Assert.Single(results!);
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
        gateway.Respond = _ => StubHttpMessageHandler.Json("""{ "results": [] }""");

        var (results, problem) = await client.SearchAsync(isbn: null, title: "Nothing Like This Exists", author: null, CancellationToken.None);

        Assert.Null(problem);
        Assert.Empty(results!);
    }

    [Fact]
    public async Task Reports_when_no_book_data_provider_could_be_reached()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        var (results, problem) = await client.SearchAsync(isbn: null, title: "Anything", author: null, CancellationToken.None);

        Assert.Null(results);
        Assert.Equal(SearchUnavailable.ProvidersUnavailable, problem);
    }

    [Fact]
    public async Task Reports_when_the_session_has_ended()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        var (results, problem) = await client.SearchAsync(isbn: null, title: "Anything", author: null, CancellationToken.None);

        Assert.Null(results);
        Assert.Equal(SearchUnavailable.NotSignedIn, problem);
    }

    [Fact]
    public async Task Reports_when_the_gateway_cannot_be_reached_at_all()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => throw new HttpRequestException("connection refused");

        var (results, problem) = await client.SearchAsync(isbn: null, title: "Anything", author: null, CancellationToken.None);

        Assert.Null(results);
        Assert.Equal(SearchUnavailable.GatewayUnreachable, problem);
    }

    [Fact]
    public async Task Searches_by_isbn_title_and_author_together()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => StubHttpMessageHandler.Json("""{ "results": [] }""");

        await client.SearchAsync(isbn: "9780380014422", title: "Lathe", author: "Le Guin", CancellationToken.None);

        var query = gateway.LastRequest!.RequestUri!.Query;
        Assert.Contains("isbn=9780380014422", query);
        Assert.Contains("title=Lathe", query);
        Assert.Contains("author=Le", query);
    }

    private static (GatewayCatalogClient Client, StubHttpMessageHandler Gateway) CreateClient()
    {
        var gateway = new StubHttpMessageHandler();
        var httpClient = new HttpClient(gateway) { BaseAddress = new Uri("http://gateway.test/") };
        return (new GatewayCatalogClient(httpClient), gateway);
    }
}
