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
        var sessionId = await LogAsync(client, entryId, 2);

        var corrected = await client.PutAsJsonAsync(
            $"/api/library/{entryId}/sessions/{sessionId}",
            new { amount = 120, durationMinutes = 45 });

        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
        var session = Assert.Single(await SessionsAsync(client, entryId));
        Assert.Equal(120, session.Amount);
        Assert.Equal(45, session.DurationMinutes);
    }

    [Fact]
    public async Task Moves_my_total_when_i_correct_a_session_it_was_added_up_from()
    {
        var client = fixture.ClientFor("correct-progress-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await LogAsync(client, entryId, 20);
        var sessionId = await LogAsync(client, entryId, 30);

        await client.PutAsJsonAsync(
            $"/api/library/{entryId}/sessions/{sessionId}",
            new { amount = 150 });

        // Progress is derived, so correcting the history is all it takes to move it.
        Assert.Equal(170, (await EntryAsync(client, entryId)).Progress!.AmountRead);
    }

    [Fact]
    public async Task Records_my_correction_in_the_units_i_am_now_tracking_in()
    {
        var client = fixture.ClientFor("switched-correction-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        var sessionId = await LogAsync(client, entryId, 60);
        await client.PutAsJsonAsync($"/api/library/{entryId}/tracking-method", new { trackingMethod = "Percentage" });

        // The reader is looking at a history written in percent — it shows this session as 20% —
        // so the 25 they type means 25%, not 25 pages.
        await client.PutAsJsonAsync($"/api/library/{entryId}/sessions/{sessionId}", new { amount = 25 });

        var session = Assert.Single(await SessionsAsync(client, entryId));
        Assert.Equal(25, session.Amount);
        Assert.Equal("Percentage", session.Unit);
        Assert.Equal(25, (await EntryAsync(client, entryId)).Progress!.AmountRead);
    }

    [Fact]
    public async Task Holds_a_correction_to_the_same_rules_as_logging()
    {
        var client = fixture.ClientFor("bad-correction-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        var sessionId = await LogAsync(client, entryId, 30);

        var nothing = await client.PutAsJsonAsync(
            $"/api/library/{entryId}/sessions/{sessionId}",
            new { amount = 0 });

        var longerThanTheBook = await client.PutAsJsonAsync(
            $"/api/library/{entryId}/sessions/{sessionId}",
            new { amount = 460 });

        Assert.Equal(HttpStatusCode.BadRequest, nothing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, longerThanTheBook.StatusCode);
        // The session the reader already had is left as it was.
        Assert.Equal(30, Assert.Single(await SessionsAsync(client, entryId)).Amount);
    }

    [Fact]
    public async Task Removes_a_session_i_logged_by_mistake()
    {
        var client = fixture.ClientFor("mistake-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        await LogAsync(client, entryId, 40, DateTimeOffset.UtcNow.AddDays(-2));
        var mistake = await LogAsync(client, entryId, 210, DateTimeOffset.UtcNow.AddDays(-1));

        var deleted = await client.DeleteAsync($"/api/library/{entryId}/sessions/{mistake}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(40, Assert.Single(await SessionsAsync(client, entryId)).Amount);
        // The total drops by exactly what the deleted session recorded.
        Assert.Equal(40, (await EntryAsync(client, entryId)).Progress!.AmountRead);
    }

    [Fact]
    public async Task Leaves_me_where_i_started_when_i_delete_the_only_session_i_had()
    {
        var client = fixture.ClientFor("only-session-reader");
        var entryId = await fixture.AddBookAsync(client, 300);
        var sessionId = await LogAsync(client, entryId, 40);

        await client.DeleteAsync($"/api/library/{entryId}/sessions/{sessionId}");

        // No sessions means no total — not zero, and not an error.
        Assert.Null((await EntryAsync(client, entryId)).Progress);
    }

    [Fact]
    public async Task Tells_me_plainly_when_the_session_i_am_fixing_is_not_there()
    {
        var client = fixture.ClientFor("gone-session-reader");
        var entryId = await fixture.AddBookAsync(client, 300);

        var corrected = await client.PutAsJsonAsync(
            $"/api/library/{entryId}/sessions/{Guid.NewGuid()}",
            new { amount = 40 });

        var deleted = await client.DeleteAsync($"/api/library/{entryId}/sessions/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, corrected.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
    }

    [Fact]
    public async Task Refuses_to_touch_someone_elses_reading()
    {
        var owner = fixture.ClientFor("session-owner");
        var entryId = await fixture.AddBookAsync(owner, 300);
        var sessionId = await LogAsync(owner, entryId, 40);
        var intruder = fixture.ClientFor("session-intruder");

        var corrected = await intruder.PutAsJsonAsync(
            $"/api/library/{entryId}/sessions/{sessionId}",
            new { amount = 299 });

        var deleted = await intruder.DeleteAsync($"/api/library/{entryId}/sessions/{sessionId}");

        Assert.Equal(HttpStatusCode.NotFound, corrected.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        Assert.Equal(40, Assert.Single(await SessionsAsync(owner, entryId)).Amount);
    }

    private static async Task<Guid> LogAsync(
        HttpClient client,
        Guid entryId,
        decimal amount,
        DateTimeOffset? occurredAt = null)
    {
        var response = await client.PostAsJsonAsync($"/api/library/{entryId}/sessions", new
        {
            amount,
            occurredAt,
        });

        return (await response.Content.ReadFromJsonAsync<Session>())!.Id;
    }

    private static async Task<IReadOnlyList<Session>> SessionsAsync(HttpClient client, Guid entryId) =>
        (await client.GetFromJsonAsync<IReadOnlyList<Session>>($"/api/library/{entryId}/sessions"))!;

    private static async Task<Entry> EntryAsync(HttpClient client, Guid entryId) =>
        (await client.GetFromJsonAsync<IReadOnlyList<Entry>>("/api/library"))!.Single(e => e.Id == entryId);

    private sealed record Entry(Guid Id, Progress? Progress);

    private sealed record Progress(decimal? AmountRead, string? Unit, int? PercentComplete);

    private sealed record Session(Guid Id, decimal Amount, string Unit, int? DurationMinutes);
}
