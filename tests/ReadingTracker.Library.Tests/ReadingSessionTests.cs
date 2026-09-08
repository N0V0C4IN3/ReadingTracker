using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Library.Tests;

[Collection(LibraryApiCollection.Name)]
public sealed class ReadingSessionTests(LibraryApiFixture fixture)
{
    [Fact]
    public async Task Records_what_i_read_and_works_out_how_far_through_i_am()
    {
        var client = ClientFor("session-reader");
        var entryId = await AddBookAsync(client, totalPages: 300);

        var logged = await client.PostAsJsonAsync($"/api/library/{entryId}/sessions", new
        {
            startPosition = 98,
            endPosition = 120,
        });

        Assert.Equal(HttpStatusCode.Created, logged.StatusCode);
        var entry = await GetEntryAsync(client, entryId);
        Assert.Equal(120, entry.Progress!.Position);
        Assert.Equal("Pages", entry.Progress.Unit);
        Assert.Equal(40m, entry.Progress.PercentComplete);
    }

    [Fact]
    public async Task Takes_my_position_from_the_most_recent_session_whatever_order_i_logged_them_in()
    {
        var client = ClientFor("latest-reader");
        var entryId = await AddBookAsync(client, totalPages: 400);

        // Logged second, but happened first.
        await LogAsync(client, entryId, 200, 260, occurredAt: DateTimeOffset.UtcNow.AddDays(-1));
        await LogAsync(client, entryId, 1, 40, occurredAt: DateTimeOffset.UtcNow.AddDays(-5));

        var entry = await GetEntryAsync(client, entryId);

        Assert.Equal(260, entry.Progress!.Position);
    }

    [Fact]
    public async Task Starts_the_book_when_i_log_reading_against_something_i_only_wanted_to_read()
    {
        var client = ClientFor("autostart-reader");
        var entryId = await AddBookAsync(client, totalPages: 300);

        await LogAsync(client, entryId, 1, 20);

        // The reader has plainly started; making them say so twice is busywork.
        Assert.Equal("Reading", (await GetEntryAsync(client, entryId)).Status);
    }

    [Fact]
    public async Task Does_not_declare_a_book_finished_just_because_i_reached_the_last_page()
    {
        var client = ClientFor("endmatter-reader");
        var entryId = await AddBookAsync(client, totalPages: 300);

        await LogAsync(client, entryId, 280, 300);

        // People stop before the end matter, and finishing is the reader's call.
        Assert.Equal("Reading", (await GetEntryAsync(client, entryId)).Status);
    }

    [Fact]
    public async Task Shows_me_the_history_of_how_i_read_a_book()
    {
        var client = ClientFor("history-reader");
        var entryId = await AddBookAsync(client, totalPages: 300);
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
        var client = ClientFor("duration-reader");
        var entryId = await AddBookAsync(client, totalPages: 300);

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
        var client = ClientFor("backwards-reader");
        var entryId = await AddBookAsync(client, totalPages: 300);

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
        var client = ClientFor("overrun-reader");
        var entryId = await AddBookAsync(client, totalPages: 300);

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
        var mine = await AddBookAsync(ClientFor("session-owner"), totalPages: 300);

        var response = await ClientFor("session-intruder")
            .PostAsJsonAsync($"/api/library/{mine}/sessions", new { startPosition = 1, endPosition = 10 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Has_no_progress_until_i_have_read_something()
    {
        var client = ClientFor("unread-reader");
        var entryId = await AddBookAsync(client, totalPages: 300);

        Assert.Null((await GetEntryAsync(client, entryId)).Progress);
    }

    private HttpClient ClientFor(string readerId)
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Reader-Id", readerId);
        return client;
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

    private async Task<Guid> AddBookAsync(HttpClient client, int totalPages)
    {
        var bookId = Guid.NewGuid();
        fixture.Catalog.Respond = request =>
        {
            var body = $$"""
                { "id": "{{bookId}}", "title": "A Book", "authors": ["A. Writer"], "totalPages": {{totalPages}} }
                """;

            return StubHttpMessageHandler.Json(
                request.RequestUri!.Query.Contains("ids=") ? $"[{body}]" : body);
        };

        var response = await client.PostAsJsonAsync("/api/library", new { bookId });
        return (await response.Content.ReadFromJsonAsync<Entry>())!.Id;
    }

    private sealed record Entry(Guid Id, Guid BookId, string Status, Progress? Progress);

    private sealed record Progress(decimal Position, string Unit, decimal? PercentComplete);

    private sealed record Session(
        Guid Id,
        decimal StartPosition,
        decimal EndPosition,
        string Unit,
        DateTimeOffset OccurredAt,
        int? DurationMinutes);
}
