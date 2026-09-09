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
        await LogAsync(client, entryId, 35);

        Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
        var progress = (await EntryAsync(client, entryId)).Progress;
        Assert.Equal(35, progress!.AmountRead);
        Assert.Equal("Percentage", progress.Unit);
        Assert.Equal(35, progress.PercentComplete);
    }

    [Fact]
    public async Task Keeps_the_pages_i_logged_exactly_as_i_logged_them_when_i_switch()
    {
        var client = fixture.ClientFor("switch-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await LogAsync(client, entryId, 150);

        await TrackByAsync(client, entryId, "Percentage");

        // The record of what the reader actually entered is untouched by the switch...
        var session = Assert.Single(await SessionsAsync(client, entryId));
        Assert.Equal(150, session.Amount);
        Assert.Equal("Pages", session.Unit);

        // ...and the conversion happens for display only.
        Assert.Equal(50, session.Displayed!.Amount);
        Assert.Equal("Percentage", session.Displayed.Unit);
    }

    [Fact]
    public async Task Shows_what_i_have_read_in_the_units_i_am_now_tracking_in()
    {
        var client = fixture.ClientFor("converted-progress-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await LogAsync(client, entryId, 150);

        await TrackByAsync(client, entryId, "Percentage");

        var progress = (await EntryAsync(client, entryId)).Progress;
        Assert.Equal(50, progress!.AmountRead);
        Assert.Equal("Percentage", progress.Unit);
    }

    [Fact]
    public async Task Converts_percentages_back_into_pages_when_i_switch_the_other_way()
    {
        var client = fixture.ClientFor("back-to-pages-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await TrackByAsync(client, entryId, "Percentage");
        await LogAsync(client, entryId, 25);

        await TrackByAsync(client, entryId, "Pages");

        var progress = (await EntryAsync(client, entryId)).Progress;
        Assert.Equal(75, progress!.AmountRead);
        Assert.Equal("Pages", progress.Unit);
        Assert.Equal("Percentage", Assert.Single(await SessionsAsync(client, entryId)).Unit);
    }

    [Fact]
    public async Task Adds_up_pages_and_percentages_i_logged_either_side_of_a_switch()
    {
        var client = fixture.ClientFor("both-units-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await LogAsync(client, entryId, 60);

        await TrackByAsync(client, entryId, "Percentage");
        await LogAsync(client, entryId, 25);

        // 60 pages is 20% of 300, and the reader has read 45% of the book between them.
        var progress = (await EntryAsync(client, entryId)).Progress;
        Assert.Equal(45, progress!.AmountRead);
        Assert.Equal("Percentage", progress.Unit);
    }

    [Fact]
    public async Task Does_not_hand_me_a_percentage_with_fifteen_decimal_places()
    {
        var client = fixture.ClientFor("tidy-percentage-reader");
        var entryId = await fixture.AddBookAsync(client, 705);
        await LogAsync(client, entryId, 350);

        await TrackByAsync(client, entryId, "Percentage");

        // 350 of 705 is 49.645390070921985...%, which is noise, not precision.
        var progress = (await EntryAsync(client, entryId)).Progress;
        Assert.Equal(49.6m, progress!.AmountRead);
        Assert.Equal(49.6m, Assert.Single(await SessionsAsync(client, entryId)).Displayed!.Amount);
    }

    [Fact]
    public async Task Keeps_the_precision_of_a_percentage_i_typed_myself()
    {
        var client = fixture.ClientFor("precise-percentage-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await TrackByAsync(client, entryId, "Percentage");

        await LogAsync(client, entryId, 17.5m);

        // Nothing is converted here, so what the reader entered is what comes back.
        Assert.Equal(17.5m, (await EntryAsync(client, entryId)).Progress!.AmountRead);
    }

    [Fact]
    public async Task Says_so_plainly_when_it_cannot_convert_without_knowing_the_length()
    {
        var client = fixture.ClientFor("unknown-length-reader");
        var entryId = await fixture.AddBookAsync(client, totalPages: null);
        await LogAsync(client, entryId, 120);

        await TrackByAsync(client, entryId, "Percentage");

        // Nobody knows how long the book is, so there is no honest percentage to give. The
        // reader is still told what they read, in the unit they actually logged.
        var progress = (await EntryAsync(client, entryId)).Progress;
        Assert.Equal(120, progress!.AmountRead);
        Assert.Equal("Pages", progress.Unit);
        Assert.Null(progress.PercentComplete);
        Assert.Null(Assert.Single(await SessionsAsync(client, entryId)).Displayed);
    }

    [Fact]
    public async Task Admits_it_cannot_total_two_units_with_no_length_to_bridge_them()
    {
        var client = fixture.ClientFor("unbridgeable-reader");
        var entryId = await fixture.AddBookAsync(client, totalPages: null);
        await LogAsync(client, entryId, 120);
        await TrackByAsync(client, entryId, "Percentage");
        await LogAsync(client, entryId, 20);

        // 120 pages and 20% cannot be added together without knowing how long the book is, and
        // answering "120" or "20" would be a smaller number than the reader's own reading.
        var progress = (await EntryAsync(client, entryId)).Progress;
        Assert.NotNull(progress);
        Assert.Null(progress.AmountRead);
        Assert.Null(progress.Unit);
        Assert.Null(progress.PercentComplete);

        // Saying how long the book is settles it: 120 of 400 is 30%, plus the 20% logged after.
        await client.PutAsJsonAsync($"/api/library/{entryId}/page-count", new { totalPages = 400 });
        Assert.Equal(50, (await EntryAsync(client, entryId)).Progress!.AmountRead);
    }

    [Fact]
    public async Task Never_says_i_have_read_more_than_all_of_a_book()
    {
        var client = fixture.ClientFor("over-hundred-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await TrackByAsync(client, entryId, "Percentage");

        var response = await LogAsync(client, entryId, 140);

        // 140% is not a share of a book anyone can have read. The session is kept as stated —
        // it may be a typo, and only the reader can say — but the total stops at all of it.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var progress = (await EntryAsync(client, entryId)).Progress;
        Assert.Equal(100, progress!.AmountRead);
        Assert.Equal(100, progress.PercentComplete);
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

    private static Task<HttpResponseMessage> LogAsync(HttpClient client, Guid entryId, decimal amount) =>
        client.PostAsJsonAsync($"/api/library/{entryId}/sessions", new { amount });

    private static async Task<Guid> AddAsync(HttpClient client, Guid bookId) =>
        (await (await client.PostAsJsonAsync("/api/library", new { bookId })).Content.ReadFromJsonAsync<Entry>())!.Id;

    private static async Task<IReadOnlyList<Session>> SessionsAsync(HttpClient client, Guid entryId) =>
        (await client.GetFromJsonAsync<IReadOnlyList<Session>>($"/api/library/{entryId}/sessions"))!;

    private static async Task<Entry> EntryAsync(HttpClient client, Guid entryId) =>
        (await client.GetFromJsonAsync<IReadOnlyList<Entry>>("/api/library"))!.Single(e => e.Id == entryId);

    private sealed record Entry(Guid Id, string TrackingMethod, Progress? Progress);

    private sealed record Progress(decimal? AmountRead, string? Unit, int? PercentComplete);

    private sealed record Session(Guid Id, decimal Amount, string Unit, DisplayedAmount? Displayed);

    private sealed record DisplayedAmount(decimal Amount, string Unit);
}
