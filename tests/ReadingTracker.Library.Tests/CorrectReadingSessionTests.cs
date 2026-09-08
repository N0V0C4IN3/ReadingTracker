using System.Net;
using System.Net.Http.Json;

namespace ReadingTracker.Library.Tests;

[Collection(LibraryApiCollection.Name)]
public sealed class CorrectReadingSessionTests(LibraryApiFixture fixture)
{
    [Fact]
    public async Task Fixes_a_session_i_typed_wrongly()
    {
        var client = fixture.ClientFor("typo-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        var sessionId = await LogAsync(client, entryId, 10, 12);

        var corrected = await client.PutAsJsonAsync(
            $"/api/library/{entryId}/sessions/{sessionId}",
            new { startPosition = 10, endPosition = 120, durationMinutes = 45 });

        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
        var session = Assert.Single(await SessionsAsync(client, entryId));
        Assert.Equal(120, session.EndPosition);
        Assert.Equal(45, session.DurationMinutes);
    }

    [Fact]
    public async Task Moves_my_progress_when_i_correct_the_session_it_came_from()
    {
        var client = fixture.ClientFor("correct-progress-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        var sessionId = await LogAsync(client, entryId, 10, 30);

        await client.PutAsJsonAsync(
            $"/api/library/{entryId}/sessions/{sessionId}",
            new { startPosition = 10, endPosition = 150 });

        // Progress is derived, so correcting the history is all it takes to move it.
        Assert.Equal(150, (await EntryAsync(client, entryId)).Progress!.Position);
    }

    [Fact]
    public async Task Puts_a_session_back_in_its_place_when_i_correct_when_it_happened()
    {
        var client = fixture.ClientFor("retime-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        var early = await LogAsync(client, entryId, 1, 40, DateTimeOffset.UtcNow.AddDays(-5));
        await LogAsync(client, entryId, 40, 90, DateTimeOffset.UtcNow.AddDays(-1));

        // The first session was actually read yesterday evening, after the other one.
        await client.PutAsJsonAsync(
            $"/api/library/{entryId}/sessions/{early}",
            new { startPosition = 1, endPosition = 40, occurredAt = DateTimeOffset.UtcNow });

        // It is now the most recent session, so the reader is back at page 40.
        Assert.Equal(40, (await EntryAsync(client, entryId)).Progress!.Position);
    }

    [Fact]
    public async Task Holds_a_correction_to_the_same_rules_as_logging()
    {
        var client = fixture.ClientFor("bad-correction-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        var sessionId = await LogAsync(client, entryId, 10, 30);

        var backwards = await client.PutAsJsonAsync(
            $"/api/library/{entryId}/sessions/{sessionId}",
            new { startPosition = 30, endPosition = 10 });

        var pastTheEnd = await client.PutAsJsonAsync(
            $"/api/library/{entryId}/sessions/{sessionId}",
            new { startPosition = 10, endPosition = 460 });

        Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, pastTheEnd.StatusCode);
        // The session the reader already had is left as it was.
        Assert.Equal(30, Assert.Single(await SessionsAsync(client, entryId)).EndPosition);
    }

    [Fact]
    public async Task Removes_a_session_i_logged_by_mistake()
    {
        var client = fixture.ClientFor("mistake-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await LogAsync(client, entryId, 1, 40, DateTimeOffset.UtcNow.AddDays(-2));
        var mistake = await LogAsync(client, entryId, 40, 250, DateTimeOffset.UtcNow.AddDays(-1));

        var deleted = await client.DeleteAsync($"/api/library/{entryId}/sessions/{mistake}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(40, Assert.Single(await SessionsAsync(client, entryId)).EndPosition);
        // Progress falls back to the session before it rather than staying where it was.
        Assert.Equal(40, (await EntryAsync(client, entryId)).Progress!.Position);
    }

    [Fact]
    public async Task Leaves_me_where_i_started_when_i_delete_the_only_session_i_had()
    {
        var client = fixture.ClientFor("only-session-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        var sessionId = await LogAsync(client, entryId, 1, 40);

        await client.DeleteAsync($"/api/library/{entryId}/sessions/{sessionId}");

        // No sessions means no position — not zero, and not an error.
        Assert.Null((await EntryAsync(client, entryId)).Progress);
    }

    [Fact]
    public async Task Tells_me_plainly_when_the_session_i_am_fixing_is_not_there()
    {
        var client = fixture.ClientFor("gone-session-reader");
        var entryId = await fixture.AddBookAsync(client, 300);

        var corrected = await client.PutAsJsonAsync(
            $"/api/library/{entryId}/sessions/{Guid.NewGuid()}",
            new { startPosition = 1, endPosition = 40 });

        var deleted = await client.DeleteAsync($"/api/library/{entryId}/sessions/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, corrected.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
    }

    [Fact]
    public async Task Refuses_to_touch_someone_elses_reading()
    {
        var owner = fixture.ClientFor("session-owner");
        var entryId = await fixture.AddBookAsync(owner, 300);
        var sessionId = await LogAsync(owner, entryId, 1, 40);
        var intruder = fixture.ClientFor("session-intruder");

        var corrected = await intruder.PutAsJsonAsync(
            $"/api/library/{entryId}/sessions/{sessionId}",
            new { startPosition = 1, endPosition = 299 });

        var deleted = await intruder.DeleteAsync($"/api/library/{entryId}/sessions/{sessionId}");

        Assert.Equal(HttpStatusCode.NotFound, corrected.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        Assert.Equal(40, Assert.Single(await SessionsAsync(owner, entryId)).EndPosition);
    }

    private static async Task<Guid> LogAsync(
        HttpClient client,
        Guid entryId,
        int start,
        int end,
        DateTimeOffset? occurredAt = null)
    {
        var response = await client.PostAsJsonAsync($"/api/library/{entryId}/sessions", new
        {
            startPosition = start,
            endPosition = end,
            occurredAt,
        });

        return (await response.Content.ReadFromJsonAsync<Session>())!.Id;
    }

    private static async Task<IReadOnlyList<Session>> SessionsAsync(HttpClient client, Guid entryId) =>
        (await client.GetFromJsonAsync<IReadOnlyList<Session>>($"/api/library/{entryId}/sessions"))!;

    private static async Task<Entry> EntryAsync(HttpClient client, Guid entryId) =>
        (await client.GetFromJsonAsync<IReadOnlyList<Entry>>("/api/library"))!.Single(e => e.Id == entryId);

    private sealed record Entry(Guid Id, Progress? Progress);

    private sealed record Progress(decimal Position, string Unit, int? PercentComplete);

    private sealed record Session(Guid Id, decimal StartPosition, decimal EndPosition, int? DurationMinutes);
}
