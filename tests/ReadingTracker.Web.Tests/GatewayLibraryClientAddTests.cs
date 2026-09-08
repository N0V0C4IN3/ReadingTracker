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

    private static (GatewayLibraryClient Client, StubHttpMessageHandler Gateway) CreateClient()
    {
        var gateway = new StubHttpMessageHandler();
        var httpClient = new HttpClient(gateway) { BaseAddress = new Uri("http://gateway.test/") };
        return (new GatewayLibraryClient(httpClient), gateway);
    }
}
