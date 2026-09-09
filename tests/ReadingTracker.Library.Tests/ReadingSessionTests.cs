using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Library.Tests;

[Collection(LibraryApiCollection.Name)]
public sealed class ReadingSessionTests(LibraryApiFixture fixture)
{
    [Fact]
    public async Task Records_what_i_read_and_works_out_how_much_of_the_book_that_is()
    {
        var client = fixture.ClientFor("session-reader");
        var entryId = await fixture.AddBookAsync(client, 300);

        var logged = await client.PostAsJsonAsync($"/api/library/{entryId}/sessions", new
        {
            amount = 120,
        });

        Assert.Equal(HttpStatusCode.Created, logged.StatusCode);
        var entry = await GetEntryAsync(client, entryId);
        Assert.Equal(120, entry.Progress!.AmountRead);
        Assert.Equal("Pages", entry.Progress.Unit);
        Assert.Equal(40, entry.Progress.PercentComplete);
    }

    [Fact]
    public async Task Adds_up_everything_i_have_read_of_a_book()
    {
        var client = fixture.ClientFor("adding-up-reader");
        var entryId = await fixture.AddBookAsync(client, 300);

        await LogAsync(client, entryId, 40);
        await LogAsync(client, entryId, 56);
        await LogAsync(client, entryId, 24);

        // A total, not a position: the reader has read 120 pages of this book.
        var entry = await GetEntryAsync(client, entryId);
        Assert.Equal(120, entry.Progress!.AmountRead);
        Assert.Equal(40, entry.Progress.PercentComplete);
    }

    [Fact]
    public async Task Adds_my_reading_up_the_same_whatever_order_i_logged_it_in()
    {
        var client = fixture.ClientFor("any-order-reader");
        var entryId = await fixture.AddBookAsync(client, 400);

        // Logged second, but happened first.
        await LogAsync(client, entryId, 60, occurredAt: DateTimeOffset.UtcNow.AddDays(-1));
        await LogAsync(client, entryId, 40, occurredAt: DateTimeOffset.UtcNow.AddDays(-5));

        // A sum has no most-recent term, which is the point: nothing turns on which session
        // happened to be logged last, or on a clock the reader may have set wrongly.
        Assert.Equal(100, (await GetEntryAsync(client, entryId)).Progress!.AmountRead);
    }

    [Fact]
    public async Task Lets_me_log_reading_a_book_i_have_already_finished_once()
    {
        var client = fixture.ClientFor("rereading-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await LogAsync(client, entryId, 300);

        var again = await LogAsync(client, entryId, 300);

        // Re-reading is reading. The total says so plainly, and the bar stops at full rather
        // than running off the end of itself.
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        var entry = await GetEntryAsync(client, entryId);
        Assert.Equal(600, entry.Progress!.AmountRead);
        Assert.Equal(100, entry.Progress.PercentComplete);
    }

    [Fact]
    public async Task Tells_me_how_far_through_i_am_in_whole_percent()
    {
        var client = fixture.ClientFor("rounding-reader");
        var entryId = await fixture.AddBookAsync(client, 705);

        await LogAsync(client, entryId, 120);

        // 17.02% is noise: nobody reads a book to two decimal places.
        Assert.Equal(17, (await GetEntryAsync(client, entryId)).Progress!.PercentComplete);
    }

    [Fact]
    public async Task Does_not_say_a_hundred_percent_while_i_still_have_a_page_to_read()
    {
        var client = fixture.ClientFor("nearly-done-reader");
        var entryId = await fixture.AddBookAsync(client, 705);

        await LogAsync(client, entryId, 704);

        // 99.86% rounds to 100, which would show a full bar on an unfinished book.
        Assert.Equal(99, (await GetEntryAsync(client, entryId)).Progress!.PercentComplete);
    }

    [Fact]
    public async Task Does_not_say_nothing_when_i_have_read_a_little_of_a_long_book()
    {
        var client = fixture.ClientFor("just-started-reader");
        var entryId = await fixture.AddBookAsync(client, 705);

        await LogAsync(client, entryId, 2);

        // 0.28% rounds to nothing, which would show an empty bar to a reader who has read.
        Assert.Equal(1, (await GetEntryAsync(client, entryId)).Progress!.PercentComplete);
    }

    [Fact]
    public async Task Starts_the_book_when_i_log_reading_against_something_i_only_wanted_to_read()
    {
        var client = fixture.ClientFor("autostart-reader");
        var entryId = await fixture.AddBookAsync(client, 300);

        await LogAsync(client, entryId, 20);

        // The reader has plainly started; making them say so twice is busywork.
        Assert.Equal("Reading", (await GetEntryAsync(client, entryId)).Status);
    }

    [Fact]
    public async Task Does_not_declare_a_book_finished_just_because_i_read_all_of_it()
    {
        var client = fixture.ClientFor("endmatter-reader");
        var entryId = await fixture.AddBookAsync(client, 300);

        await LogAsync(client, entryId, 300);

        // People stop before the end matter, and finishing is the reader's call.
        Assert.Equal("Reading", (await GetEntryAsync(client, entryId)).Status);
    }

    [Fact]
    public async Task Shows_me_the_history_of_how_i_read_a_book()
    {
        var client = fixture.ClientFor("history-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await LogAsync(client, entryId, 40, occurredAt: DateTimeOffset.UtcNow.AddDays(-2));
        await LogAsync(client, entryId, 56, occurredAt: DateTimeOffset.UtcNow.AddDays(-1));

        var sessions = await client.GetFromJsonAsync<IReadOnlyList<Session>>($"/api/library/{entryId}/sessions");

        Assert.Equal(2, sessions!.Count);
        Assert.Contains(sessions, session => session.Amount == 40);
        Assert.Contains(sessions, session => session.Amount == 56);
    }

    [Fact]
    public async Task Records_how_long_i_read_for_when_i_say_so()
    {
        var client = fixture.ClientFor("duration-reader");
        var entryId = await fixture.AddBookAsync(client, 300);

        await client.PostAsJsonAsync($"/api/library/{entryId}/sessions", new
        {
            amount = 20,
            durationMinutes = 45,
        });

        var sessions = await client.GetFromJsonAsync<IReadOnlyList<Session>>($"/api/library/{entryId}/sessions");
        Assert.Equal(45, Assert.Single(sessions!).DurationMinutes);
    }

    [Fact]
    public async Task Refuses_a_session_in_which_nothing_was_read()
    {
        var client = fixture.ClientFor("nothing-read-reader");
        var entryId = await fixture.AddBookAsync(client, 300);

        var nothing = await LogAsync(client, entryId, 0);
        var backwards = await LogAsync(client, entryId, -20);

        Assert.Equal(HttpStatusCode.BadRequest, nothing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);
        Assert.Null((await GetEntryAsync(client, entryId)).Progress);
    }

    [Fact]
    public async Task Refuses_a_sitting_longer_than_the_whole_book()
    {
        var client = fixture.ClientFor("overrun-reader");
        var entryId = await fixture.AddBookAsync(client, 300);

        var response = await LogAsync(client, entryId, 460);

        // Nobody reads 460 pages of a 300-page book in one sitting; they mistyped.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Takes_any_amount_when_nobody_knows_how_long_the_book_is()
    {
        var client = fixture.ClientFor("unmeasured-reader");
        var entryId = await fixture.AddBookAsync(client, totalPages: null);

        var response = await LogAsync(client, entryId, 460);

        // There is no length to have read more than, and losing the reading would be worse
        // than losing the check.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(460, (await GetEntryAsync(client, entryId)).Progress!.AmountRead);
    }

    [Fact]
    public async Task Refuses_to_log_reading_against_someone_elses_shelf()
    {
        var mine = await fixture.AddBookAsync(fixture.ClientFor("session-owner"), 300);

        var response = await fixture.ClientFor("session-intruder")
            .PostAsJsonAsync($"/api/library/{mine}/sessions", new { amount = 10 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Has_no_progress_until_i_have_read_something()
    {
        var client = fixture.ClientFor("unread-reader");
        var entryId = await fixture.AddBookAsync(client, 300);

        Assert.Null((await GetEntryAsync(client, entryId)).Progress);
    }

    [Fact]
    public async Task Records_when_i_read_from_a_timezone_that_is_not_utc()
    {
        var client = fixture.ClientFor("moscow-reader");
        var entryId = await fixture.AddBookAsync(client, 300);

        // What a browser east of UTC actually sends for "I read this on the 8th": local midnight
        // carrying its own offset. Postgres accepts only a zero offset through Npgsql, so storing
        // this as it arrives fails the whole request — every test until now happened to use
        // UtcNow, which is exactly why nothing caught it.
        var whenIRead = new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.FromHours(3));

        var logged = await LogAsync(client, entryId, 60, whenIRead);

        Assert.Equal(HttpStatusCode.Created, logged.StatusCode);

        var sessions = await client.GetFromJsonAsync<IReadOnlyList<Session>>($"/api/library/{entryId}/sessions");

        // The same instant, whatever offset it is written with.
        Assert.Equal(whenIRead, Assert.Single(sessions!).OccurredAt);
    }

    [Fact]
    public async Task Corrects_a_session_to_a_time_in_a_timezone_that_is_not_utc()
    {
        var client = fixture.ClientFor("corrections-abroad-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await LogAsync(client, entryId, 40);

        var sessionId = (await client.GetFromJsonAsync<IReadOnlyList<Session>>($"/api/library/{entryId}/sessions"))!
            .Single().Id;

        var actually = new DateTimeOffset(2026, 9, 7, 21, 30, 0, TimeSpan.FromHours(-5));

        var corrected = await client.PutAsJsonAsync($"/api/library/{entryId}/sessions/{sessionId}", new
        {
            amount = 40,
            occurredAt = actually,
        });

        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);

        var sessions = await client.GetFromJsonAsync<IReadOnlyList<Session>>($"/api/library/{entryId}/sessions");
        Assert.Equal(actually, Assert.Single(sessions!).OccurredAt);
    }

    private static Task<HttpResponseMessage> LogAsync(
        HttpClient client,
        Guid entryId,
        decimal amount,
        DateTimeOffset? occurredAt = null) =>
        client.PostAsJsonAsync($"/api/library/{entryId}/sessions", new
        {
            amount,
            occurredAt,
        });

    private static async Task<Entry> GetEntryAsync(HttpClient client, Guid entryId) =>
        (await client.GetFromJsonAsync<IReadOnlyList<Entry>>("/api/library"))!.Single(e => e.Id == entryId);

    private sealed record Entry(Guid Id, Guid BookId, string Status, Progress? Progress);

    private sealed record Progress(decimal? AmountRead, string? Unit, int? PercentComplete);

    private sealed record Session(
        Guid Id,
        decimal Amount,
        string Unit,
        DateTimeOffset OccurredAt,
        int? DurationMinutes);
}
