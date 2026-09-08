using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Library.Tests;

[Collection(LibraryApiCollection.Name)]
public sealed class ReadingSessionTests(LibraryApiFixture fixture)
{
    [Fact]
    public async Task Records_what_i_read_and_works_out_how_far_through_i_am()
    {
        var client = fixture.ClientFor("session-reader");
        var entryId = await fixture.AddBookAsync(client,300);

        var logged = await client.PostAsJsonAsync($"/api/library/{entryId}/sessions", new
        {
            startPosition = 98,
            endPosition = 120,
        });

        Assert.Equal(HttpStatusCode.Created, logged.StatusCode);
        var entry = await GetEntryAsync(client, entryId);
        Assert.Equal(120, entry.Progress!.Position);
        Assert.Equal("Pages", entry.Progress.Unit);
        Assert.Equal(40, entry.Progress.PercentComplete);
    }

    [Fact]
    public async Task Tells_me_how_far_through_i_am_in_whole_percent()
    {
        var client = fixture.ClientFor("rounding-reader");
        var entryId = await fixture.AddBookAsync(client,705);

        await LogAsync(client, entryId, 1, 120);

        // 17.02% is noise: nobody reads a book to two decimal places.
        Assert.Equal(17, (await GetEntryAsync(client, entryId)).Progress!.PercentComplete);
    }

    [Fact]
    public async Task Does_not_say_a_hundred_percent_while_i_still_have_a_page_to_read()
    {
        var client = fixture.ClientFor("nearly-done-reader");
        var entryId = await fixture.AddBookAsync(client,705);

        await LogAsync(client, entryId, 700, 704);

        // 99.86% rounds to 100, which would show a full bar on an unfinished book.
        Assert.Equal(99, (await GetEntryAsync(client, entryId)).Progress!.PercentComplete);
    }

    [Fact]
    public async Task Does_not_say_nothing_when_i_have_read_a_little_of_a_long_book()
    {
        var client = fixture.ClientFor("just-started-reader");
        var entryId = await fixture.AddBookAsync(client,705);

        await LogAsync(client, entryId, 1, 2);

        // 0.28% rounds to nothing, which would show an empty bar to a reader who has read.
        Assert.Equal(1, (await GetEntryAsync(client, entryId)).Progress!.PercentComplete);
    }

    [Fact]
    public async Task Takes_my_position_from_the_most_recent_session_whatever_order_i_logged_them_in()
    {
        var client = fixture.ClientFor("latest-reader");
        var entryId = await fixture.AddBookAsync(client,400);

        // Logged second, but happened first.
        await LogAsync(client, entryId, 200, 260, occurredAt: DateTimeOffset.UtcNow.AddDays(-1));
        await LogAsync(client, entryId, 1, 40, occurredAt: DateTimeOffset.UtcNow.AddDays(-5));

        var entry = await GetEntryAsync(client, entryId);

        Assert.Equal(260, entry.Progress!.Position);
    }

    [Fact]
    public async Task Starts_the_book_when_i_log_reading_against_something_i_only_wanted_to_read()
    {
        var client = fixture.ClientFor("autostart-reader");
        var entryId = await fixture.AddBookAsync(client,300);

        await LogAsync(client, entryId, 1, 20);

        // The reader has plainly started; making them say so twice is busywork.
        Assert.Equal("Reading", (await GetEntryAsync(client, entryId)).Status);
    }

    [Fact]
    public async Task Does_not_declare_a_book_finished_just_because_i_reached_the_last_page()
    {
        var client = fixture.ClientFor("endmatter-reader");
        var entryId = await fixture.AddBookAsync(client,300);

        await LogAsync(client, entryId, 280, 300);

        // People stop before the end matter, and finishing is the reader's call.
        Assert.Equal("Reading", (await GetEntryAsync(client, entryId)).Status);
    }

    [Fact]
    public async Task Shows_me_the_history_of_how_i_read_a_book()
    {
        var client = fixture.ClientFor("history-reader");
        var entryId = await fixture.AddBookAsync(client,300);
        await LogAsync(client, entryId, 1, 40, occurredAt: DateTimeOffset.UtcNow.AddDays(-2));
        await LogAsync(client, entryId, 40, 96, occurredAt: DateTimeOffset.UtcNow.AddDays(-1));

        var sessions = await client.GetFromJsonAsync<IReadOnlyList<Session>>($"/api/library/{entryId}/sessions");

        Assert.Equal(2, sessions!.Count);
        Assert.Contains(sessions, session => session.StartPosition == 1 && session.EndPosition == 40);
        Assert.Contains(sessions, session => session.StartPosition == 40 && session.EndPosition == 96);
    }

    [Fact]
    public async Task Records_how_long_i_read_for_when_i_say_so()
    {
        var client = fixture.ClientFor("duration-reader");
        var entryId = await fixture.AddBookAsync(client,300);

        await client.PostAsJsonAsync($"/api/library/{entryId}/sessions", new
        {
            startPosition = 10,
            endPosition = 30,
            durationMinutes = 45,
        });

        var sessions = await client.GetFromJsonAsync<IReadOnlyList<Session>>($"/api/library/{entryId}/sessions");
        Assert.Equal(45, Assert.Single(sessions!).DurationMinutes);
    }

    [Fact]
    public async Task Refuses_a_session_that_ends_before_it_starts()
    {
        var client = fixture.ClientFor("backwards-reader");
        var entryId = await fixture.AddBookAsync(client,300);

        var response = await client.PostAsJsonAsync($"/api/library/{entryId}/sessions", new
        {
            startPosition = 120,
            endPosition = 98,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Refuses_a_session_that_runs_past_the_end_of_the_book()
    {
        var client = fixture.ClientFor("overrun-reader");
        var entryId = await fixture.AddBookAsync(client,300);

        var response = await client.PostAsJsonAsync($"/api/library/{entryId}/sessions", new
        {
            startPosition = 290,
            endPosition = 460,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Refuses_to_log_reading_against_someone_elses_shelf()
    {
        var mine = await fixture.AddBookAsync(fixture.ClientFor("session-owner"), 300);

        var response = await fixture.ClientFor("session-intruder")
            .PostAsJsonAsync($"/api/library/{mine}/sessions", new { startPosition = 1, endPosition = 10 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Has_no_progress_until_i_have_read_something()
    {
        var client = fixture.ClientFor("unread-reader");
        var entryId = await fixture.AddBookAsync(client,300);

        Assert.Null((await GetEntryAsync(client, entryId)).Progress);
    }

    private static Task<HttpResponseMessage> LogAsync(
        HttpClient client,
        Guid entryId,
        int start,
        int end,
        DateTimeOffset? occurredAt = null) =>
        client.PostAsJsonAsync($"/api/library/{entryId}/sessions", new
        {
            startPosition = start,
            endPosition = end,
            occurredAt,
        });

    private static async Task<Entry> GetEntryAsync(HttpClient client, Guid entryId) =>
        (await client.GetFromJsonAsync<IReadOnlyList<Entry>>("/api/library"))!.Single(e => e.Id == entryId);

    private sealed record Entry(Guid Id, Guid BookId, string Status, Progress? Progress);

    private sealed record Progress(decimal Position, string Unit, int? PercentComplete);

    private sealed record Session(
        Guid Id,
        decimal StartPosition,
        decimal EndPosition,
        string Unit,
        DateTimeOffset OccurredAt,
        int? DurationMinutes);
}
