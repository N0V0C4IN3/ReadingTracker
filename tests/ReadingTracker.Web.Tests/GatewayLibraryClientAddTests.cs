using System.Net;
using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

public sealed class GatewayLibraryClientAddTests
{
    [Fact]
    public async Task Adds_a_book_to_the_library()
    {
        var (client, gateway) = CreateClient();
        var bookId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        gateway.Respond = _ => StubHttpMessageHandler.Json($$"""
            {
              "id": "{{entryId}}",
              "bookId": "{{bookId}}",
              "status": "WantToRead",
              "trackingMethod": "Pages",
              "book": null,
              "progress": null
            }
            """);

        var (entry, problem) = await client.AddAsync(bookId, CancellationToken.None);

        Assert.Null(problem);
        Assert.Equal(entryId, entry!.Id);
        Assert.Equal(bookId, entry.BookId);
        Assert.Equal("WantToRead", entry.Status);
    }

    [Fact]
    public async Task Reports_when_the_book_is_already_in_the_library()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.Conflict);

        var (entry, problem) = await client.AddAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(entry);
        Assert.Equal(AddToLibraryProblem.AlreadyInLibrary, problem);
    }

    [Fact]
    public async Task Reports_when_the_session_has_ended()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        var (entry, problem) = await client.AddAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(entry);
        Assert.Equal(AddToLibraryProblem.NotSignedIn, problem);
    }

    [Fact]
    public async Task Reports_when_the_gateway_cannot_be_reached_at_all()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => throw new HttpRequestException("connection refused");

        var (entry, problem) = await client.AddAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(entry);
        Assert.Equal(AddToLibraryProblem.GatewayUnreachable, problem);
    }

    [Fact]
    public async Task Adding_as_want_to_read_is_the_add_and_nothing_more()
    {
        var (client, gateway) = CreateClient();
        var bookId = Guid.NewGuid();
        gateway.Respond = request => Entry(bookId, "WantToRead");

        var shelved = await client.AddAsAsync(bookId, ReadingStatuses.OnAdding, CancellationToken.None);

        Assert.Equal("WantToRead", shelved.Entry!.Status);
        Assert.False(shelved.StatusRefused);
        Assert.Single(gateway.Requests);
    }

    [Fact]
    public async Task Adding_as_another_status_moves_the_book_there_once_it_is_on()
    {
        var (client, gateway) = CreateClient();
        var bookId = Guid.NewGuid();
        gateway.Respond = request => request.RequestUri!.AbsolutePath.EndsWith("/status")
            ? Entry(bookId, "Reading")
            : Entry(bookId, "WantToRead");

        var shelved = await client.AddAsAsync(bookId, "Reading", CancellationToken.None);

        Assert.Null(shelved.Problem);
        Assert.False(shelved.StatusRefused);
        Assert.Equal("Reading", shelved.Entry!.Status);
        Assert.Contains("\"Reading\"", await gateway.Requests[1].Content!.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_status_that_cannot_be_set_leaves_the_book_on_want_to_read_and_says_so()
    {
        var (client, gateway) = CreateClient();
        var bookId = Guid.NewGuid();
        gateway.Respond = request => request.RequestUri!.AbsolutePath.EndsWith("/status")
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : Entry(bookId, "WantToRead");

        var shelved = await client.AddAsFinishedAsync(bookId, new FinishedDay.Today(), CancellationToken.None);

        Assert.Null(shelved.Problem);
        Assert.True(shelved.StatusRefused);
        Assert.Equal("WantToRead", shelved.Entry!.Status);
    }

    [Fact]
    public async Task A_book_that_could_not_be_added_is_not_moved_anywhere()
    {
        var (client, gateway) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.Conflict);

        var shelved = await client.AddAsAsync(Guid.NewGuid(), "Reading", CancellationToken.None);

        Assert.Null(shelved.Entry);
        Assert.Equal(AddToLibraryProblem.AlreadyInLibrary, shelved.Problem);
        Assert.Single(gateway.Requests);
    }

    private static HttpResponseMessage Entry(Guid bookId, string status) => StubHttpMessageHandler.Json($$"""
        {
          "id": "33333333-3333-3333-3333-333333333333",
          "bookId": "{{bookId}}",
          "status": "{{status}}",
          "trackingMethod": "Pages"
        }
        """);

    private static (GatewayLibraryClient Client, StubHttpMessageHandler Gateway) CreateClient()
    {
        var gateway = new StubHttpMessageHandler();
        var httpClient = new HttpClient(gateway) { BaseAddress = new Uri("http://gateway.test/") };
        return (new GatewayLibraryClient(httpClient, new ShelfChanges(), TimeProvider.System), gateway);
    }
}
