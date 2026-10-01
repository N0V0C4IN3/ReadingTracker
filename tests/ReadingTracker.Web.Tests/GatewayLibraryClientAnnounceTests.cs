using System.Net;
using ReadingTracker.Web.Services;

namespace ReadingTracker.Web.Tests;

/// <summary>
/// The client tells the shelf when a change may have moved the count of finished books behind the
/// header's goal badge — a status that took, an entry removed — and says nothing otherwise. It is
/// the client that says so, so no card, page or import has to remember to.
/// </summary>
public sealed class GatewayLibraryClientAnnounceTests
{
    private static readonly Guid EntryId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BookId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task A_status_that_took_is_announced_once()
    {
        var (client, gateway, announced) = CreateClient();
        gateway.Respond = _ => StubHttpMessageHandler.Json(Entry("Finished"));

        var outcome = await client.SetStatusAsync(EntryId, "Finished", CancellationToken.None);

        Assert.True(outcome.Ok);
        Assert.Equal(1, announced.Count);
    }

    [Fact]
    public async Task A_removed_entry_is_announced_once()
    {
        var (client, gateway, announced) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(HttpStatusCode.NoContent);

        var outcome = await client.RemoveAsync(EntryId, CancellationToken.None);

        Assert.True(outcome.Ok);
        Assert.Equal(1, announced.Count);
    }

    [Fact]
    public async Task A_book_added_straight_as_finished_is_announced_once()
    {
        var (client, gateway, announced) = CreateClient();
        gateway.Respond = request => request.RequestUri!.AbsolutePath.EndsWith("/status")
            ? StubHttpMessageHandler.Json(Entry("Finished"))
            : StubHttpMessageHandler.Json(Entry("WantToRead"));

        var shelved = await client.AddAsAsync(BookId, "Finished", CancellationToken.None);

        Assert.Equal("Finished", shelved.Entry!.Status);
        Assert.Equal(1, announced.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task A_status_that_did_not_take_is_not_announced(HttpStatusCode answer)
    {
        var (client, gateway, announced) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(answer);

        var outcome = await client.SetStatusAsync(EntryId, "Finished", CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Equal(0, announced.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task An_entry_that_was_not_removed_is_not_announced(HttpStatusCode answer)
    {
        var (client, gateway, announced) = CreateClient();
        gateway.Respond = _ => new HttpResponseMessage(answer);

        var outcome = await client.RemoveAsync(EntryId, CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Equal(0, announced.Count);
    }

    [Fact]
    public async Task A_gateway_that_cannot_be_reached_announces_nothing()
    {
        var (client, gateway, announced) = CreateClient();
        gateway.Respond = _ => throw new HttpRequestException("connection refused");

        await client.SetStatusAsync(EntryId, "Finished", CancellationToken.None);
        await client.RemoveAsync(EntryId, CancellationToken.None);

        Assert.Equal(0, announced.Count);
    }

    [Fact]
    public async Task A_status_refused_after_a_book_was_added_announces_nothing()
    {
        var (client, gateway, announced) = CreateClient();
        gateway.Respond = request => request.RequestUri!.AbsolutePath.EndsWith("/status")
            ? new HttpResponseMessage(HttpStatusCode.BadRequest)
            : StubHttpMessageHandler.Json(Entry("WantToRead"));

        var shelved = await client.AddAsAsync(BookId, "Finished", CancellationToken.None);

        Assert.True(shelved.StatusRefused);
        Assert.Equal(0, announced.Count);
    }

    [Fact]
    public async Task A_plain_add_is_not_announced_because_it_always_lands_on_want_to_read()
    {
        var (client, gateway, announced) = CreateClient();
        gateway.Respond = _ => StubHttpMessageHandler.Json(Entry("WantToRead"));

        await client.AddAsync(BookId, CancellationToken.None);

        Assert.Equal(0, announced.Count);
    }

    [Fact]
    public async Task Changes_that_cannot_move_the_count_are_not_announced()
    {
        var (client, gateway, announced) = CreateClient();
        gateway.Respond = request => request.RequestUri!.AbsolutePath.EndsWith("/sessions")
            ? StubHttpMessageHandler.Json($$"""{ "session": {{Session()}}, "entry": {{Entry("Reading")}} }""")
            : StubHttpMessageHandler.Json(Entry("Reading"));

        await client.LogSessionAsync(EntryId, new NewSession(10, null, null), CancellationToken.None);
        await client.SetTrackingMethodAsync(EntryId, "Percentage", CancellationToken.None);
        await client.SetPageCountAsync(EntryId, 300, CancellationToken.None);

        Assert.Equal(0, announced.Count);
    }

    [Fact]
    public async Task Reads_are_not_announced()
    {
        var (client, gateway, announced) = CreateClient();
        gateway.Respond = request => request.RequestUri!.AbsolutePath.Contains("/goals/")
            ? StubHttpMessageHandler.Json("""{ "year": 2026, "books": 12, "finished": 3 }""")
            : StubHttpMessageHandler.Json($"[{Entry("Finished")}]");

        await client.GetEntriesAsync(null, CancellationToken.None);
        await client.GetGoalAsync(2026, CancellationToken.None);
        await client.GetSessionsAsync(EntryId, CancellationToken.None);

        Assert.Equal(0, announced.Count);
    }

    private static (GatewayLibraryClient Client, StubHttpMessageHandler Gateway, Announcements Announced) CreateClient()
    {
        var gateway = new StubHttpMessageHandler();
        var httpClient = new HttpClient(gateway) { BaseAddress = new Uri("http://gateway.test/") };
        var shelf = new ShelfChanges();
        var announced = new Announcements();
        shelf.Changed += () => announced.Count++;
        return (new GatewayLibraryClient(httpClient, shelf), gateway, announced);
    }

    private sealed class Announcements
    {
        public int Count { get; set; }
    }

    private static string Entry(string status) =>
        $$"""
        {
          "id": "{{EntryId}}",
          "bookId": "{{BookId}}",
          "status": "{{status}}",
          "trackingMethod": "Pages",
          "book": null,
          "progress": null
        }
        """;

    private static string Session() =>
        """
        {
          "id": "22222222-2222-2222-2222-222222222222",
          "amount": 10,
          "unit": "Pages",
          "source": "Reader",
          "occurredAt": "2026-09-08T00:00:00+00:00",
          "durationMinutes": null,
          "displayed": null
        }
        """;
}
