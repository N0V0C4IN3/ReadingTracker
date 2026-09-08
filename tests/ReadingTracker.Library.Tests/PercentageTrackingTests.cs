using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Library.Tests;

[Collection(LibraryApiCollection.Name)]
public sealed class PercentageTrackingTests(LibraryApiFixture fixture)
{
    [Fact]
    public async Task Tracks_an_ebook_by_percentage_when_i_say_to()
    {
        var client = fixture.ClientFor("ebook-reader");
        var entryId = await fixture.AddBookAsync(client, 300);

        var switched = await TrackByAsync(client, entryId, "Percentage");
        await LogAsync(client, entryId, 0, 35);

        Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
        var progress = (await EntryAsync(client, entryId)).Progress;
        Assert.Equal(35, progress!.Position);
        Assert.Equal("Percentage", progress.Unit);
        Assert.Equal(35, progress.PercentComplete);
    }

    [Fact]
    public async Task Keeps_the_pages_i_logged_exactly_as_i_logged_them_when_i_switch()
    {
        var client = fixture.ClientFor("switch-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await LogAsync(client, entryId, 1, 150);

        await TrackByAsync(client, entryId, "Percentage");

        // The record of what the reader actually entered is untouched by the switch...
        var session = Assert.Single(await SessionsAsync(client, entryId));
        Assert.Equal(150, session.EndPosition);
        Assert.Equal("Pages", session.Unit);

        // ...and the conversion happens for display only.
        Assert.Equal(50, session.Displayed!.EndPosition);
        Assert.Equal("Percentage", session.Displayed.Unit);
    }

    [Fact]
    public async Task Shows_where_i_am_in_the_units_i_am_now_tracking_in()
    {
        var client = fixture.ClientFor("converted-progress-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await LogAsync(client, entryId, 1, 150);

        await TrackByAsync(client, entryId, "Percentage");

        var progress = (await EntryAsync(client, entryId)).Progress;
        Assert.Equal(50, progress!.Position);
        Assert.Equal("Percentage", progress.Unit);
    }

    [Fact]
    public async Task Converts_percentages_back_into_pages_when_i_switch_the_other_way()
    {
        var client = fixture.ClientFor("back-to-pages-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await TrackByAsync(client, entryId, "Percentage");
        await LogAsync(client, entryId, 0, 25);

        await TrackByAsync(client, entryId, "Pages");

        var progress = (await EntryAsync(client, entryId)).Progress;
        Assert.Equal(75, progress!.Position);
        Assert.Equal("Pages", progress.Unit);
        Assert.Equal("Percentage", Assert.Single(await SessionsAsync(client, entryId)).Unit);
    }

    [Fact]
    public async Task Says_so_plainly_when_it_cannot_convert_without_knowing_the_length()
    {
        var client = fixture.ClientFor("unknown-length-reader");
        var entryId = await fixture.AddBookAsync(client, totalPages: null);
        await LogAsync(client, entryId, 1, 120);

        await TrackByAsync(client, entryId, "Percentage");

        // Nobody knows how long the book is, so there is no honest percentage to give. The
        // reader is still told where they are, in the unit they actually logged.
        var progress = (await EntryAsync(client, entryId)).Progress;
        Assert.Equal(120, progress!.Position);
        Assert.Equal("Pages", progress.Unit);
        Assert.Null(progress.PercentComplete);
        Assert.Null(Assert.Single(await SessionsAsync(client, entryId)).Displayed);
    }

    [Fact]
    public async Task Refuses_a_percentage_past_a_hundred()
    {
        var client = fixture.ClientFor("over-hundred-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await TrackByAsync(client, entryId, "Percentage");

        var response = await LogAsync(client, entryId, 90, 140);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Lets_two_readers_track_the_same_book_differently()
    {
        var bookId = Guid.NewGuid();
        fixture.CatalogHasBook(bookId, 300);
        var pages = fixture.ClientFor("pages-reader");
        var percent = fixture.ClientFor("percent-reader");
        var mine = await AddAsync(pages, bookId);
        var theirs = await AddAsync(percent, bookId);

        await TrackByAsync(percent, theirs, "Percentage");

        // The tracking method belongs to the reader's entry, not to the shared Book.
        Assert.Equal("Pages", (await EntryAsync(pages, mine)).TrackingMethod);
        Assert.Equal("Percentage", (await EntryAsync(percent, theirs)).TrackingMethod);
    }

    [Fact]
    public async Task Refuses_to_change_how_someone_else_tracks_their_book()
    {
        var entryId = await fixture.AddBookAsync(fixture.ClientFor("tracking-owner"), 300);

        var response = await TrackByAsync(fixture.ClientFor("tracking-intruder"), entryId, "Percentage");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_a_way_of_tracking_that_does_not_exist()
    {
        var client = fixture.ClientFor("bad-method-reader");
        var entryId = await fixture.AddBookAsync(client, 300);

        var response = await TrackByAsync(client, entryId, "Chapters");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static Task<HttpResponseMessage> TrackByAsync(HttpClient client, Guid entryId, string method) =>
        client.PutAsJsonAsync($"/api/library/{entryId}/tracking-method", new { trackingMethod = method });

    private static Task<HttpResponseMessage> LogAsync(HttpClient client, Guid entryId, decimal start, decimal end) =>
        client.PostAsJsonAsync($"/api/library/{entryId}/sessions", new
        {
            startPosition = start,
            endPosition = end,
        });

    private static async Task<Guid> AddAsync(HttpClient client, Guid bookId) =>
        (await (await client.PostAsJsonAsync("/api/library", new { bookId })).Content.ReadFromJsonAsync<Entry>())!.Id;

    private static async Task<IReadOnlyList<Session>> SessionsAsync(HttpClient client, Guid entryId) =>
        (await client.GetFromJsonAsync<IReadOnlyList<Session>>($"/api/library/{entryId}/sessions"))!;

    private static async Task<Entry> EntryAsync(HttpClient client, Guid entryId) =>
        (await client.GetFromJsonAsync<IReadOnlyList<Entry>>("/api/library"))!.Single(e => e.Id == entryId);

    private sealed record Entry(Guid Id, string TrackingMethod, Progress? Progress);

    private sealed record Progress(decimal Position, string Unit, int? PercentComplete);

    private sealed record Session(Guid Id, decimal EndPosition, string Unit, DisplayedPosition? Displayed);

    private sealed record DisplayedPosition(decimal StartPosition, decimal EndPosition, string Unit);
}
