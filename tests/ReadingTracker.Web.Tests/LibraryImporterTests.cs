using System.Net;
using System.Text.Json;
using ReadingTracker.TestSupport;
using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

/// <summary>
/// One imported book, through the same doors a reader uses by hand: found in the Catalog by its
/// ISBN or added there, put on the shelf, then moved to its status with the day it was finished.
/// </summary>
public class LibraryImporterTests
{
    private static readonly Guid BookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EntryId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly ImportedBook Algernon =
        new("Flowers for Algernon", ["Daniel Keyes"], "9780156030083", 311, "Finished", new DateOnly(2026, 5, 15));

    [Fact]
    public async Task A_book_the_catalog_knows_is_shelved_and_finished_on_its_day()
    {
        var (importer, gateway, waits) = Create(request => request.RequestUri!.PathAndQuery switch
        {
            var path when path.StartsWith("/api/books/search") => Json($$"""{"results":[{"id":"{{BookId}}","title":"Flowers for Algernon","authors":["Daniel Keyes"],"totalPages":311}],"page":1,"hasMore":false}"""),
            "/api/library" => Json($$"""{"id":"{{EntryId}}","bookId":"{{BookId}}","status":"WantToRead","trackingMethod":"Pages","addedAt":"2026-09-20T00:00:00Z"}""", HttpStatusCode.Created),
            var path when path.EndsWith("/status") => Json($$"""{"id":"{{EntryId}}","bookId":"{{BookId}}","status":"Finished","trackingMethod":"Pages","addedAt":"2026-09-20T00:00:00Z","finishedOn":"2026-05-15"}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        var result = await importer.ImportAsync(Algernon, CancellationToken.None);

        Assert.Equal(ImportOutcome.Added, result.Outcome);
        var search = gateway.Requests[0];
        Assert.Contains("q=9780156030083", search.RequestUri!.Query);
        var status = gateway.Requests.Single(r => r.RequestUri!.PathAndQuery.EndsWith("/status"));
        var body = JsonDocument.Parse(await status.Content!.ReadAsStringAsync()).RootElement;
        Assert.Equal("Finished", body.GetProperty("status").GetString());
        Assert.Equal("2026-05-15", body.GetProperty("finishedOn").GetString());
        Assert.DoesNotContain(gateway.Requests, r => r.RequestUri!.PathAndQuery.EndsWith("/page-count"));
        Assert.Empty(waits);
    }

    [Fact]
    public async Task A_book_nobody_has_heard_of_is_added_by_hand_with_what_hardcover_knew()
    {
        var (importer, gateway, _) = Create(request => request.RequestUri!.PathAndQuery switch
        {
            var path when path.StartsWith("/api/books/search") => Json("""{"results":[],"page":1,"hasMore":false}"""),
            "/api/books" => Json($$"""{"id":"{{BookId}}","title":"Flowers for Algernon","authors":["Daniel Keyes"],"totalPages":311}""", HttpStatusCode.Created),
            "/api/library" => Json($$"""{"id":"{{EntryId}}","bookId":"{{BookId}}","status":"WantToRead","trackingMethod":"Pages","addedAt":"2026-09-20T00:00:00Z"}""", HttpStatusCode.Created),
            var path when path.EndsWith("/status") => Json($$"""{"id":"{{EntryId}}","bookId":"{{BookId}}","status":"Finished","trackingMethod":"Pages","addedAt":"2026-09-20T00:00:00Z"}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        var result = await importer.ImportAsync(Algernon, CancellationToken.None);

        Assert.Equal(ImportOutcome.Added, result.Outcome);
        var created = gateway.Requests.Single(r => r.RequestUri!.PathAndQuery == "/api/books");
        var body = JsonDocument.Parse(await created.Content!.ReadAsStringAsync()).RootElement;
        Assert.Equal("Flowers for Algernon", body.GetProperty("title").GetString());
        Assert.Equal("9780156030083", body.GetProperty("isbn").GetString());
        Assert.Equal(311, body.GetProperty("totalPages").GetInt32());
    }

    [Fact]
    public async Task A_book_already_on_the_shelf_is_left_exactly_as_it_is()
    {
        var (importer, gateway, _) = Create(request => request.RequestUri!.PathAndQuery switch
        {
            var path when path.StartsWith("/api/books/search") => Json($$"""{"results":[{"id":"{{BookId}}","title":"Flowers for Algernon","authors":["Daniel Keyes"],"totalPages":311}],"page":1,"hasMore":false}"""),
            "/api/library" => new HttpResponseMessage(HttpStatusCode.Conflict),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        var result = await importer.ImportAsync(Algernon, CancellationToken.None);

        Assert.Equal(ImportOutcome.AlreadyOnShelf, result.Outcome);
        Assert.DoesNotContain(gateway.Requests, r => r.RequestUri!.PathAndQuery.EndsWith("/status"));
    }

    [Fact]
    public async Task Waits_out_the_gateways_pace_and_tries_again()
    {
        var searches = 0;
        var (importer, _, waits) = Create(request => request.RequestUri!.PathAndQuery switch
        {
            var path when path.StartsWith("/api/books/search") => ++searches == 1
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                : Json($$"""{"results":[{"id":"{{BookId}}","title":"Flowers for Algernon","authors":["Daniel Keyes"],"totalPages":311}],"page":1,"hasMore":false}"""),
            "/api/library" => Json($$"""{"id":"{{EntryId}}","bookId":"{{BookId}}","status":"WantToRead","trackingMethod":"Pages","addedAt":"2026-09-20T00:00:00Z"}""", HttpStatusCode.Created),
            var path when path.EndsWith("/status") => Json($$"""{"id":"{{EntryId}}","bookId":"{{BookId}}","status":"Finished","trackingMethod":"Pages","addedAt":"2026-09-20T00:00:00Z"}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        var result = await importer.ImportAsync(Algernon, CancellationToken.None);

        Assert.Equal(ImportOutcome.Added, result.Outcome);
        Assert.Equal(2, searches);
        Assert.Single(waits);
    }

    [Fact]
    public async Task Sets_the_page_count_hardcover_knew_when_the_catalog_does_not()
    {
        var (importer, gateway, _) = Create(request => request.RequestUri!.PathAndQuery switch
        {
            var path when path.StartsWith("/api/books/search") => Json($$"""{"results":[{"id":"{{BookId}}","title":"Flowers for Algernon","authors":["Daniel Keyes"],"totalPages":null}],"page":1,"hasMore":false}"""),
            "/api/library" => Json($$"""{"id":"{{EntryId}}","bookId":"{{BookId}}","status":"WantToRead","trackingMethod":"Pages","addedAt":"2026-09-20T00:00:00Z"}""", HttpStatusCode.Created),
            var path when path.EndsWith("/status") || path.EndsWith("/page-count") => Json($$"""{"id":"{{EntryId}}","bookId":"{{BookId}}","status":"Finished","trackingMethod":"Pages","addedAt":"2026-09-20T00:00:00Z"}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        await importer.ImportAsync(Algernon, CancellationToken.None);

        var pageCount = gateway.Requests.Single(r => r.RequestUri!.PathAndQuery.EndsWith("/page-count"));
        Assert.Contains("311", await pageCount.Content!.ReadAsStringAsync());
    }

    [Fact]
    public async Task Says_when_a_book_could_not_be_put_on_the_shelf()
    {
        var (importer, _, _) = Create(request => request.RequestUri!.PathAndQuery switch
        {
            var path when path.StartsWith("/api/books/search") => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        var result = await importer.ImportAsync(Algernon, CancellationToken.None);

        Assert.Equal(ImportOutcome.Failed, result.Outcome);
        Assert.NotNull(result.Note);
    }

    private static (LibraryImporter Importer, StubHttpMessageHandler Gateway, List<TimeSpan> Waits) Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var gateway = new StubHttpMessageHandler { Respond = respond };
        var http = new HttpClient(gateway) { BaseAddress = new Uri("http://gateway.test/") };
        var waits = new List<TimeSpan>();
        var importer = new LibraryImporter(
            new GatewayCatalogClient(http),
            new GatewayLibraryClient(http),
            (wait, _) => { waits.Add(wait); return Task.CompletedTask; });
        return (importer, gateway, waits);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
}
